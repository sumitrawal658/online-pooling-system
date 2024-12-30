import React, { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
    Container,
    VStack,
    Heading,
    Button,
    Text,
    useToast,
    Box,
    Divider,
} from '@chakra-ui/react';
import { ChevronLeftIcon } from '@chakra-ui/icons';
import { pollService } from '../services/api';
import { Poll } from '../types';
import { PollResults } from '../components/PollResults';
import { VoteSection } from '../components/VoteSection';

export const PollDetailsPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const toast = useToast();
    const [poll, setPoll] = useState<Poll | null>(null);
    const [hasVoted, setHasVoted] = useState(false);

    useEffect(() => {
        const fetchPoll = async () => {
            try {
                if (id) {
                    const data = await pollService.getPoll(id);
                    setPoll(data);
                }
            } catch (error) {
                toast({
                    title: 'Error',
                    description: 'Failed to load poll',
                    status: 'error',
                });
            }
        };
        fetchPoll();
    }, [id, toast]);

    const handleVote = async (optionId: string) => {
        try {
            if (poll) {
                await pollService.vote(poll.pollId, optionId);
                setHasVoted(true);
                const updatedPoll = await pollService.getPoll(poll.pollId);
                setPoll(updatedPoll);
                toast({
                    title: 'Success',
                    description: 'Vote recorded successfully',
                    status: 'success',
                });
            }
        } catch (error) {
            toast({
                title: 'Error',
                description: 'Failed to record vote',
                status: 'error',
            });
        }
    };

    if (!poll) {
        return (
            <Container centerContent py={8}>
                <Text>Loading...</Text>
            </Container>
        );
    }

    return (
        <Container maxW="container.lg" py={8}>
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
                    <Heading size="lg">{poll.title}</Heading>
                </Box>

                <Divider />

                {!hasVoted ? (
                    <VoteSection poll={poll} onVote={handleVote} />
                ) : (
                    <PollResults poll={poll} />
                )}
            </VStack>
        </Container>
    );
}; 