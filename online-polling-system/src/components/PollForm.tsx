import { useState } from 'react';
import {
    Box,
    Button,
    FormControl,
    FormLabel,
    Input,
    Stack,
    IconButton,
    useToast
} from '@chakra-ui/react';
import { AddIcon, DeleteIcon } from '@chakra-ui/icons';
import { CreatePollRequest } from '../types';

interface PollFormProps {
    onSubmit: (data: CreatePollRequest) => Promise<void>;
}

export const PollForm = ({ onSubmit }: PollFormProps) => {
    const [title, setTitle] = useState('');
    const [options, setOptions] = useState(['', '']);
    const [startDate, setStartDate] = useState('');
    const [endDate, setEndDate] = useState('');
    const toast = useToast();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (options.some(opt => !opt.trim())) {
            toast({
                title: 'Error',
                description: 'All options must have a value',
                status: 'error'
            });
            return;
        }

        try {
            await onSubmit({
                title,
                options: options.filter(opt => opt.trim()),
                startDate,
                endDate: endDate || undefined
            });
        } catch (error) {
            toast({
                title: 'Error',
                description: 'Failed to create poll',
                status: 'error'
            });
        }
    };

    return (
        <Box as="form" onSubmit={handleSubmit}>
            <Stack spacing={4}>
                <FormControl isRequired>
                    <FormLabel>Question</FormLabel>
                    <Input
                        value={title}
                        onChange={(e) => setTitle(e.target.value)}
                        placeholder="What's your question?"
                    />
                </FormControl>

                {options.map((option, index) => (
                    <FormControl key={index} isRequired>
                        <FormLabel>Option {index + 1}</FormLabel>
                        <Stack direction="row">
                            <Input
                                value={option}
                                onChange={(e) => {
                                    const newOptions = [...options];
                                    newOptions[index] = e.target.value;
                                    setOptions(newOptions);
                                }}
                                placeholder={`Option ${index + 1}`}
                            />
                            {options.length > 2 && (
                                <IconButton
                                    aria-label="Delete option"
                                    icon={<DeleteIcon />}
                                    onClick={() => {
                                        setOptions(options.filter((_, i) => i !== index));
                                    }}
                                />
                            )}
                        </Stack>
                    </FormControl>
                ))}

                <Button
                    leftIcon={<AddIcon />}
                    onClick={() => setOptions([...options, ''])}
                    variant="outline"
                >
                    Add Option
                </Button>

                <FormControl isRequired>
                    <FormLabel>Start Date</FormLabel>
                    <Input
                        type="date"
                        value={startDate}
                        onChange={(e) => setStartDate(e.target.value)}
                    />
                </FormControl>

                <FormControl>
                    <FormLabel>End Date (Optional)</FormLabel>
                    <Input
                        type="date"
                        value={endDate}
                        onChange={(e) => setEndDate(e.target.value)}
                    />
                </FormControl>

                <Button type="submit" colorScheme="blue">
                    Create Poll
                </Button>
            </Stack>
        </Box>
    );
}; 