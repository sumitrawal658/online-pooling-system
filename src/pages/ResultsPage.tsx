import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
    Container,
    VStack,
    Heading,
    Button,
    useToast,
    Box,
    Spinner,
    Text,
} from '@chakra-ui/react';
import { ChevronLeftIcon } from '@chakra-ui/icons';
import { useRealtimePoll } from '../hooks/useRealtimePoll';
import { PollResults } from '../components/PollResults';

export const ResultsPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const { poll, isLoading, error } = useRealtimePoll(id!);

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
                    {poll && <Heading size="lg">{poll.title}</Heading>}
                </Box>

                {poll && <PollResults poll={poll} />}
            </VStack>
        </Container>
    );
}; 