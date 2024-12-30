import { Box, Text, Badge, Stack } from '@chakra-ui/react';
import { format } from 'date-fns';
import { Poll } from '../types';

interface PollCardProps {
    poll: Poll;
    onClick: () => void;
}

export const PollCard = ({ poll, onClick }: PollCardProps) => {
    return (
        <Box
            p={5}
            shadow="md"
            borderWidth="1px"
            borderRadius="lg"
            cursor="pointer"
            onClick={onClick}
            _hover={{ shadow: 'lg' }}
        >
            <Stack spacing={2}>
                <Text fontSize="xl" fontWeight="semibold">
                    {poll.title}
                </Text>
                <Stack direction="row" spacing={2}>
                    <Badge colorScheme={poll.isActive ? 'green' : 'red'}>
                        {poll.isActive ? 'Active' : 'Closed'}
                    </Badge>
                    <Badge colorScheme="blue">
                        {poll.options.length} options
                    </Badge>
                </Stack>
                <Text fontSize="sm" color="gray.600">
                    Created {format(new Date(poll.createdAt), 'PPP')}
                </Text>
                {poll.endDate && (
                    <Text fontSize="sm" color="gray.600">
                        Ends {format(new Date(poll.endDate), 'PPP')}
                    </Text>
                )}
            </Stack>
        </Box>
    );
}; 