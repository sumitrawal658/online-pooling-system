import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import axios from 'axios';

const PollList = () => {
    const [polls, setPolls] = useState([]);

    useEffect(() => {
        const fetchPolls = async () => {
            try {
                const response = await axios.get('/api/poll');
                setPolls(response.data);
            } catch (error) {
                console.error('Error fetching polls:', error);
            }
        };
        fetchPolls();
    }, []);

    return (
        <div className="max-w-2xl mx-auto p-4">
            <div className="flex justify-between items-center mb-6">
                <h2 className="text-2xl font-bold">Active Polls</h2>
                <Link
                    to="/create"
                    className="bg-blue-500 text-white px-4 py-2 rounded hover:bg-blue-600"
                >
                    Create New Poll
                </Link>
            </div>

            <div className="space-y-4">
                {polls.map(poll => (
                    <Link
                        key={poll.pollId}
                        to={`/poll/${poll.pollId}`}
                        className="block p-4 border rounded hover:bg-gray-50"
                    >
                        <h3 className="font-semibold">{poll.title}</h3>
                        <div className="text-sm text-gray-600">
                            {poll.options.length} options · 
                            Created {new Date(poll.createdAt).toLocaleDateString()}
                            {poll.endDate && ` · Ends ${new Date(poll.endDate).toLocaleDateString()}`}
                        </div>
                    </Link>
                ))}
            </div>
        </div>
    );
};

export default PollList; 