import React, { useState } from 'react';
import {
    VStack,
    Button,
    RadioGroup,
    Radio,
    Text,
} from '@chakra-ui/react';
import { Poll } from '../types';

interface VoteOptionsProps {
    poll: Poll;
    onVote: (optionId: string) => Promise<void>;
}

export const VoteOptions: React.FC<VoteOptionsProps> = ({ poll, onVote }) => {
    const [selectedOption, setSelectedOption] = useState<string>('');
    const [isSubmitting, setIsSubmitting] = useState(false);

    const handleSubmit = async () => {
        if (!selectedOption) return;
        setIsSubmitting(true);
        try {
            await onVote(selectedOption);
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <VStack spacing={6} align="stretch">
            <Text fontSize="lg">Choose your answer:</Text>
            <RadioGroup value={selectedOption} onChange={setSelectedOption}>
                <VStack spacing={4} align="stretch">
                    {poll.options.map((option) => (
                        <Radio key={option.optionId} value={option.optionId}>
                            {option.optionText}
                        </Radio>
                    ))}
                </VStack>
            </RadioGroup>
            <Button
                colorScheme="blue"
                isDisabled={!selectedOption}
                isLoading={isSubmitting}
                onClick={handleSubmit}
            >
                Submit Vote
            </Button>
        </VStack>
    );
}; 