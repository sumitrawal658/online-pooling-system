import { useEffect } from 'react';
import { usePoll } from '../context/PollContext';

export function useRealtimePoll(pollId: string) {
    const { 
        activePoll,
        isLoading,
        error,
        fetchPoll
    } = usePoll();

    useEffect(() => {
        fetchPoll(pollId);
    }, [pollId, fetchPoll]);

    return {
        poll: activePoll,
        isLoading,
        error
    };
} 