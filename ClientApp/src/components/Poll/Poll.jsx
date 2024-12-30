import React, { useState, useEffect, useCallback } from 'react';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { Chart as ChartJS } from 'chart.js/auto';
import { Bar, Pie } from 'react-chartjs-2';
import { motion } from 'framer-motion';
import './Poll.css';

const Poll = ({ pollId }) => {
    const [poll, setPoll] = useState(null);
    const [selectedOption, setSelectedOption] = useState(null);
    const [hasVoted, setHasVoted] = useState(false);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [connection, setConnection] = useState(null);

    // Fetch poll data
    useEffect(() => {
        const fetchPoll = async () => {
            try {
                const response = await fetch(`/api/polls/${pollId}`);
                if (!response.ok) throw new Error('Failed to fetch poll');
                const data = await response.json();
                setPoll(data);
                setLoading(false);
            } catch (err) {
                setError(err.message);
                setLoading(false);
            }
        };

        fetchPoll();
    }, [pollId]);

    // Set up real-time connection
    useEffect(() => {
        const newConnection = new HubConnectionBuilder()
            .withUrl('/pollHub')
            .withAutomaticReconnect()
            .build();

        setConnection(newConnection);

        return () => {
            if (connection) {
                connection.stop();
            }
        };
    }, []);

    // Start the connection
    useEffect(() => {
        if (connection) {
            connection.start()
                .then(() => {
                    console.log('Connected to PollHub');
                    connection.on('ReceiveVote', (updatedPoll) => {
                        setPoll(updatedPoll);
                    });
                })
                .catch(err => console.error('Error connecting to PollHub:', err));
        }
    }, [connection]);

    const handleVote = async () => {
        if (!selectedOption || hasVoted) return;

        try {
            const response = await fetch(`/api/polls/${pollId}/vote`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ optionId: selectedOption }),
            });

            if (!response.ok) throw new Error('Failed to submit vote');

            const updatedPoll = await response.json();
            setPoll(updatedPoll);
            setHasVoted(true);
            localStorage.setItem(`voted_${pollId}`, 'true');
        } catch (err) {
            setError(err.message);
        }
    };

    const renderChart = () => {
        if (!poll) return null;

        const data = {
            labels: poll.options.map(option => option.text),
            datasets: [{
                data: poll.options.map(option => option.votes),
                backgroundColor: [
                    '#FF6384', '#36A2EB', '#FFCE56', '#4BC0C0',
                    '#9966FF', '#FF9F40', '#FF6384', '#36A2EB'
                ],
            }],
        };

        const options = {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'bottom',
                },
                title: {
                    display: true,
                    text: 'Poll Results',
                    font: {
                        size: 16,
                    },
                },
            },
        };

        return (
            <div className="chart-container">
                <div className="bar-chart">
                    <Bar data={data} options={options} />
                </div>
                <div className="pie-chart">
                    <Pie data={data} options={options} />
                </div>
            </div>
        );
    };

    if (loading) return <div className="loading">Loading poll...</div>;
    if (error) return <div className="error">{error}</div>;
    if (!poll) return <div className="error">Poll not found</div>;

    return (
        <motion.div 
            className="poll-container"
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5 }}
        >
            <h2 className="poll-title">{poll.title}</h2>
            {!hasVoted ? (
                <>
                    <div className="options-container">
                        {poll.options.map(option => (
                            <motion.button
                                key={option.id}
                                className={`option-button ${selectedOption === option.id ? 'selected' : ''}`}
                                onClick={() => setSelectedOption(option.id)}
                                whileHover={{ scale: 1.02 }}
                                whileTap={{ scale: 0.98 }}
                            >
                                {option.text}
                            </motion.button>
                        ))}
                    </div>
                    <motion.button
                        className="vote-button"
                        onClick={handleVote}
                        disabled={!selectedOption}
                        whileHover={{ scale: 1.05 }}
                        whileTap={{ scale: 0.95 }}
                    >
                        Submit Vote
                    </motion.button>
                </>
            ) : (
                <div className="results-container">
                    {renderChart()}
                    <div className="stats">
                        <p>Total Votes: {poll.totalVotes}</p>
                        <p>Poll ends: {new Date(poll.endDate).toLocaleDateString()}</p>
                    </div>
                </div>
            )}
        </motion.div>
    );
};

export default Poll; 