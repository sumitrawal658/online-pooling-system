export interface Poll {
    pollId: string;
    title: string;
    createdBy: string;
    startDate: string;
    endDate?: string;
    createdAt: string;
    isActive: boolean;
    options: PollOption[];
}

export interface PollOption {
    optionId: string;
    pollId: string;
    optionText: string;
    voteCount: number;
}

export interface CreatePollRequest {
    title: string;
    options: string[];
    startDate: string;
    endDate?: string;
}

export interface VoteRequest {
    pollId: string;
    optionId: string;
} 