import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Box, Button, Container, Heading, SimpleGrid, VStack } from '@chakra-ui/react';
import { PollCard } from '../components/PollCard';
import { pollService } from '../services/api';
import { Poll } from '../types';

export const HomePage = () => {
    const [polls, setPolls] = useState<Poll[]>([]);
    const navigate = useNavigate();

    useEffect(() => {
        const fetchPolls = async () => {
            const data = await pollService.getActivePolls();
            setPolls(data);
        };
        fetchPolls();
    }, []);

    return (
        <Container maxW="container.xl" py={8}>
            <VStack spacing={8} align="stretch">
                <Box display="flex" justifyContent="space-between" alignItems="center">
                    <Heading>Active Polls</Heading>
                    <Button colorScheme="blue" onClick={() => navigate('/create')}>
                        Create Poll
                    </Button>
                </Box>

                <SimpleGrid columns={{ base: 1, md: 2, lg: 3 }} spacing={6}>
                    {polls.map(poll => (
                        <PollCard
                            key={poll.pollId}
                            poll={poll}
                            onClick={() => navigate(`/poll/${poll.pollId}`)}
                        />
                    ))}
                </SimpleGrid>
            </VStack>
        </Container>
    );
}; 