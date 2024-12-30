import React, { useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
    Container,
    VStack,
    Heading,
    Button,
    useToast,
    Box,
    Spinner,
} from '@chakra-ui/react';
import { ChevronLeftIcon } from '@chakra-ui/icons';
import { usePoll } from '../context/PollContext';
import { VoteOptions } from '../components/VoteOptions';

export const VotePage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const toast = useToast();
    const { activePoll, isLoading, error, fetchPoll, votePoll } = usePoll();

    useEffect(() => {
        if (id) {
            fetchPoll(id).catch((error) => {
                toast({
                    title: 'Error',
                    description: 'Failed to load poll',
                    status: 'error',
                });
            });
        }
    }, [id, fetchPoll, toast]);

    const handleVote = async (optionId: string) => {
        try {
            if (activePoll) {
                await votePoll(activePoll.pollId, optionId);
                toast({
                    title: 'Success',
                    description: 'Vote recorded successfully',
                    status: 'success',
                });
                navigate(`/poll/${activePoll.pollId}/results`);
            }
        } catch (error) {
            toast({
                title: 'Error',
                description: 'Failed to record vote',
                status: 'error',
            });
        }
    };

    if (isLoading) {
        return (
            <Container centerContent py={8}>
                <Spinner size="xl" />
            </Container>
        );
    }

    if (error) {
        return (
            <Container centerContent py={8}>
                <Text color="red.500">{error}</Text>
            </Container>
        );
    }

    return (
        <Container maxW="container.md" py={8}>
            <VStack spacing={8} align="stretch">
                <Box>
                    <Button
                        leftIcon={<ChevronLeftIcon />}
                        variant="ghost"
                        onClick={() => navigate('/')}
                        mb={4}
                    >
                        Back to Polls
                    </Button>
                    {activePoll && <Heading size="lg">{activePoll.title}</Heading>}
                </Box>

                {activePoll && <VoteOptions poll={activePoll} onVote={handleVote} />}
            </VStack>
        </Container>
    );
}; 