import React from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import PollList from './components/PollList';
import PollCreate from './components/PollCreate';
import PollVote from './components/PollVote';

const App = () => {
    return (
        <Router>
            <div className="min-h-screen bg-gray-50">
                <nav className="bg-white shadow mb-8">
                    <div className="max-w-7xl mx-auto px-4 py-4">
                        <Link to="/" className="text-xl font-bold text-gray-800">
                            Polling System
                        </Link>
                    </div>
                </nav>

                <Routes>
                    <Route path="/" element={<PollList />} />
                    <Route path="/create" element={<PollCreate />} />
                    <Route path="/poll/:pollId" element={<PollVote />} />
                </Routes>
            </div>
        </Router>
    );
};

export default App; 