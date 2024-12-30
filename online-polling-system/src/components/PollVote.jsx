import React, { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import axios from 'axios';

const PollVote = () => {
    const { pollId } = useParams();
    const [poll, setPoll] = useState(null);
    const [selectedOption, setSelectedOption] = useState(null);
    const [hasVoted, setHasVoted] = useState(false);

    useEffect(() => {
        const fetchPoll = async () => {
            try {
                const response = await axios.get(`/api/poll/${pollId}`);
                setPoll(response.data);
            } catch (error) {
                console.error('Error fetching poll:', error);
            }
        };
        fetchPoll();
    }, [pollId]);

    const handleVote = async () => {
        try {
            await axios.post('/api/poll/vote', {
                pollId,
                optionId: selectedOption
            });
            setHasVoted(true);
            // Refresh poll data to show updated results
            const response = await axios.get(`/api/poll/${pollId}/results`);
            setPoll(response.data);
        } catch (error) {
            console.error('Error voting:', error);
        }
    };

    if (!poll) return <div>Loading...</div>;

    return (
        <div className="max-w-2xl mx-auto p-4">
            <h2 className="text-2xl font-bold mb-4">{poll.title}</h2>
            {!hasVoted ? (
                <div className="space-y-4">
                    {poll.options.map(option => (
                        <div
                            key={option.optionId}
                            className={`p-4 border rounded cursor-pointer ${
                                selectedOption === option.optionId ? 'bg-blue-100' : ''
                            }`}
                            onClick={() => setSelectedOption(option.optionId)}
                        >
                            {option.optionText}
                        </div>
                    ))}
                    <button
                        onClick={handleVote}
                        disabled={!selectedOption}
                        className="bg-blue-500 text-white px-6 py-2 rounded hover:bg-blue-600 disabled:bg-gray-300"
                    >
                        Submit Vote
                    </button>
                </div>
            ) : (
                <PollResults poll={poll} />
            )}
        </div>
    );
};

export default PollVote; 