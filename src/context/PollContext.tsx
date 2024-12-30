import React, { createContext, useContext, useReducer, useEffect, useRef } from 'react';
import { Poll, CreatePollRequest } from '../types';
import { pollService } from '../services/api';
import { WebSocketService } from '../services/websocket';

interface PollState {
    polls: Poll[];
    activePoll: Poll | null;
    isLoading: boolean;
    error: string | null;
}

type PollAction =
    | { type: 'SET_POLLS'; payload: Poll[] }
    | { type: 'SET_ACTIVE_POLL'; payload: Poll }
    | { type: 'UPDATE_POLL'; payload: Poll }
    | { type: 'SET_LOADING'; payload: boolean }
    | { type: 'SET_ERROR'; payload: string }
    | { type: 'CLEAR_ERROR' };

interface PollContextType extends PollState {
    fetchPolls: () => Promise<void>;
    fetchPoll: (id: string) => Promise<void>;
    createPoll: (data: CreatePollRequest) => Promise<Poll>;
    votePoll: (pollId: string, optionId: string) => Promise<void>;
    startPolling: (pollId: string) => void;
    stopPolling: () => void;
}

const PollContext = createContext<PollContextType | undefined>(undefined);

const initialState: PollState = {
    polls: [],
    activePoll: null,
    isLoading: false,
    error: null,
};

function pollReducer(state: PollState, action: PollAction): PollState {
    switch (action.type) {
        case 'SET_POLLS':
            return { ...state, polls: action.payload, isLoading: false };
        case 'SET_ACTIVE_POLL':
            return { ...state, activePoll: action.payload, isLoading: false };
        case 'UPDATE_POLL':
            return {
                ...state,
                activePoll: action.payload,
                polls: state.polls.map(poll =>
                    poll.pollId === action.payload.pollId ? action.payload : poll
                ),
            };
        case 'SET_LOADING':
            return { ...state, isLoading: action.payload };
        case 'SET_ERROR':
            return { ...state, error: action.payload, isLoading: false };
        case 'CLEAR_ERROR':
            return { ...state, error: null };
        default:
            return state;
    }
}

interface WebSocketMessage {
    type: 'POLL_UPDATED' | 'NEW_VOTE' | 'POLL_DELETED';
    payload: any;
}

export const PollProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [state, dispatch] = useReducer(pollReducer, initialState);
    const wsRef = useRef<WebSocketService | null>(null);

    useEffect(() => {
        // Initialize WebSocket
        const ws = new WebSocketService(import.meta.env.VITE_WS_URL || 'ws://localhost:5000/ws');
        wsRef.current = ws;
        ws.connect();

        // Subscribe to WebSocket events
        ws.subscribe('POLL_UPDATED', (poll: Poll) => {
            dispatch({ type: 'UPDATE_POLL', payload: poll });
        });

        ws.subscribe('NEW_VOTE', async (data: { pollId: string }) => {
            if (state.activePoll?.pollId === data.pollId) {
                const updatedPoll = await pollService.getPoll(data.pollId);
                dispatch({ type: 'UPDATE_POLL', payload: updatedPoll });
            }
        });

        return () => {
            ws.disconnect();
        };
    }, []);

    const fetchPolls = async () => {
        dispatch({ type: 'SET_LOADING', payload: true });
        try {
            const polls = await pollService.getActivePolls();
            dispatch({ type: 'SET_POLLS', payload: polls });
        } catch (error) {
            dispatch({ type: 'SET_ERROR', payload: 'Failed to fetch polls' });
        }
    };

    const fetchPoll = async (id: string) => {
        dispatch({ type: 'SET_LOADING', payload: true });
        try {
            const poll = await pollService.getPoll(id);
            dispatch({ type: 'SET_ACTIVE_POLL', payload: poll });
            // Subscribe to poll updates
            wsRef.current?.send('SUBSCRIBE_POLL', { pollId: id });
        } catch (error) {
            dispatch({ type: 'SET_ERROR', payload: 'Failed to fetch poll' });
        }
    };

    const createPoll = async (data: CreatePollRequest) => {
        dispatch({ type: 'SET_LOADING', payload: true });
        try {
            const poll = await pollService.createPoll(data);
            await fetchPolls(); // Refresh polls list
            return poll;
        } catch (error) {
            dispatch({ type: 'SET_ERROR', payload: 'Failed to create poll' });
            throw error;
        }
    };

    const votePoll = async (pollId: string, optionId: string) => {
        dispatch({ type: 'SET_LOADING', payload: true });
        try {
            await pollService.vote(pollId, optionId);
            const updatedPoll = await pollService.getPoll(pollId);
            dispatch({ type: 'UPDATE_POLL', payload: updatedPoll });
            // Notify other clients about the vote
            wsRef.current?.send('NEW_VOTE', { pollId, optionId });
        } catch (error) {
            dispatch({ type: 'SET_ERROR', payload: 'Failed to submit vote' });
            throw error;
        }
    };

    const startPolling = (pollId: string) => {
        stopPolling(); // Clear any existing interval
        pollInterval = setInterval(async () => {
            try {
                const poll = await pollService.getPoll(pollId);
                dispatch({ type: 'UPDATE_POLL', payload: poll });
            } catch (error) {
                console.error('Polling error:', error);
            }
        }, 5000); // Poll every 5 seconds
    };

    const stopPolling = () => {
        if (pollInterval) {
            clearInterval(pollInterval);
            pollInterval = null;
        }
    };

    useEffect(() => {
        fetchPolls();
        return () => stopPolling();
    }, []);

    const value = {
        ...state,
        fetchPolls,
        fetchPoll,
        createPoll,
        votePoll,
        startPolling,
        stopPolling,
    };

    return <PollContext.Provider value={value}>{children}</PollContext.Provider>;
};

export const usePoll = () => {
    const context = useContext(PollContext);
    if (context === undefined) {
        throw new Error('usePoll must be used within a PollProvider');
    }
    return context;
}; 