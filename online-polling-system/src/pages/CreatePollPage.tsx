import React from 'react';
import { Container, Heading, VStack, useToast } from '@chakra-ui/react';
import { useNavigate } from 'react-router-dom';
import { PollForm } from '../components/PollForm';
import { pollService } from '../services/api';
import { CreatePollRequest } from '../types';

export const CreatePollPage: React.FC = () => {
    const navigate = useNavigate();
    const toast = useToast();

    const handleSubmit = async (data: CreatePollRequest) => {
        try {
            const poll = await pollService.createPoll(data);
            toast({
                title: 'Success',
                description: 'Poll created successfully',
                status: 'success',
                duration: 3000,
            });
            navigate(`/poll/${poll.pollId}`);
        } catch (error) {
            toast({
                title: 'Error',
                description: 'Failed to create poll',
                status: 'error',
                duration: 3000,
            });
        }
    };

    return (
        <Container maxW="container.md" py={8}>
            <VStack spacing={8} align="stretch">
                <Heading>Create New Poll</Heading>
                <PollForm onSubmit={handleSubmit} />
            </VStack>
        </Container>
    );
}; 