import React from 'react';
import { ChakraProvider } from '@chakra-ui/react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { PollProvider } from './context/PollContext';
import { HomePage } from './pages/HomePage';
import { CreatePollPage } from './pages/CreatePollPage';
import { VotePage } from './pages/VotePage';
import { ResultsPage } from './pages/ResultsPage';

const App: React.FC = () => {
    return (
        <ChakraProvider>
            <PollProvider>
                <Router>
                    <Routes>
                        <Route path="/" element={<HomePage />} />
                        <Route path="/create" element={<CreatePollPage />} />
                        <Route path="/poll/:id" element={<VotePage />} />
                        <Route path="/poll/:id/results" element={<ResultsPage />} />
                    </Routes>
                </Router>
            </PollProvider>
        </ChakraProvider>
    );
};

export default App; 