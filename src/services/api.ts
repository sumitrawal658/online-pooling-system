import axios from 'axios';
import { CreatePollRequest, Poll, VoteRequest } from '../types';

const api = axios.create({
    baseURL: import.meta.env.VITE_API_URL || 'http://localhost:5000/api'
});

export const pollService = {
    createPoll: async (data: CreatePollRequest) => {
        const response = await api.post<Poll>('/poll', data);
        return response.data;
    },

    getPoll: async (id: string) => {
        const response = await api.get<Poll>(`/poll/${id}`);
        return response.data;
    },

    getActivePolls: async () => {
        const response = await api.get<Poll[]>('/poll');
        return response.data;
    },

    vote: async (pollId: string, optionId: string) => {
        const data: VoteRequest = { pollId, optionId };
        const response = await api.post(`/poll/${pollId}/vote`, data);
        return response.data;
    }
}; 