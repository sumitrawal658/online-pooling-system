import React, { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Link } from 'react-router-dom';
import './PollList.css';

const PollList = () => {
    const [polls, setPolls] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [filter, setFilter] = useState('active'); // active, expired, all
    const [searchTerm, setSearchTerm] = useState('');
    const [sortBy, setSortBy] = useState('newest'); // newest, popular, ending-soon

    useEffect(() => {
        fetchPolls();
    }, [filter, sortBy]);

    const fetchPolls = async () => {
        try {
            const response = await fetch(`/api/polls?filter=${filter}&sort=${sortBy}`);
            if (!response.ok) throw new Error('Failed to fetch polls');
            const data = await response.json();
            setPolls(data);
            setLoading(false);
        } catch (err) {
            setError(err.message);
            setLoading(false);
        }
    };

    const filteredPolls = polls.filter(poll =>
        poll.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
        poll.description?.toLowerCase().includes(searchTerm.toLowerCase())
    );

    const getTimeRemaining = (endDate) => {
        const end = new Date(endDate);
        const now = new Date();
        const diff = end - now;

        if (diff <= 0) return 'Expired';

        const days = Math.floor(diff / (1000 * 60 * 60 * 24));
        const hours = Math.floor((diff % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));

        if (days > 0) return `${days}d ${hours}h remaining`;
        return `${hours}h remaining`;
    };

    if (loading) return <div className="loading">Loading polls...</div>;
    if (error) return <div className="error">{error}</div>;

    return (
        <div className="poll-list-container">
            <div className="poll-list-header">
                <h1>Polls</h1>
                <div className="poll-controls">
                    <input
                        type="text"
                        placeholder="Search polls..."
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        className="search-input"
                    />
                    <select
                        value={filter}
                        onChange={(e) => setFilter(e.target.value)}
                        className="filter-select"
                    >
                        <option value="active">Active Polls</option>
                        <option value="expired">Expired Polls</option>
                        <option value="all">All Polls</option>
                    </select>
                    <select
                        value={sortBy}
                        onChange={(e) => setSortBy(e.target.value)}
                        className="sort-select"
                    >
                        <option value="newest">Newest First</option>
                        <option value="popular">Most Popular</option>
                        <option value="ending-soon">Ending Soon</option>
                    </select>
                </div>
            </div>

            <AnimatePresence>
                <motion.div className="polls-grid">
                    {filteredPolls.map(poll => (
                        <motion.div
                            key={poll.id}
                            className="poll-card"
                            initial={{ opacity: 0, y: 20 }}
                            animate={{ opacity: 1, y: 0 }}
                            exit={{ opacity: 0, y: -20 }}
                            whileHover={{ scale: 1.02 }}
                            transition={{ duration: 0.2 }}
                        >
                            <Link to={`/poll/${poll.id}`} className="poll-link">
                                <h3>{poll.title}</h3>
                                <div className="poll-meta">
                                    <span className="vote-count">
                                        {poll.totalVotes} votes
                                    </span>
                                    <span className={`time-remaining ${
                                        new Date(poll.endDate) <= new Date() ? 'expired' : ''
                                    }`}>
                                        {getTimeRemaining(poll.endDate)}
                                    </span>
                                </div>
                                <div className="poll-preview">
                                    {poll.options.slice(0, 2).map(option => (
                                        <div key={option.id} className="option-preview">
                                            {option.text}
                                        </div>
                                    ))}
                                    {poll.options.length > 2 && (
                                        <div className="more-options">
                                            +{poll.options.length - 2} more options
                                        </div>
                                    )}
                                </div>
                            </Link>
                        </motion.div>
                    ))}
                </motion.div>
            </AnimatePresence>

            {filteredPolls.length === 0 && (
                <div className="no-results">
                    No polls found matching your criteria
                </div>
            )}
        </div>
    );
};

export default PollList; 