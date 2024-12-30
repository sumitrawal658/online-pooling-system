import React, { useState } from 'react';
import {
    VStack,
    Radio,
    RadioGroup,
    Button,
    Text,
} from '@chakra-ui/react';
import { Poll } from '../types';

interface VoteSectionProps {
    poll: Poll;
    onVote: (optionId: string) => Promise<void>;
}

export const VoteSection: React.FC<VoteSectionProps> = ({ poll, onVote }) => {
    const [selectedOption, setSelectedOption] = useState<string>('');
    const [isVoting, setIsVoting] = useState(false);

    const handleVote = async () => {
        if (!selectedOption) return;
        setIsVoting(true);
        try {
            await onVote(selectedOption);
        } finally {
            setIsVoting(false);
        }
    };

    return (
        <VStack spacing={6} align="stretch">
            <Text fontSize="lg" fontWeight="medium">
                Choose an option:
            </Text>
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
                isLoading={isVoting}
                onClick={handleVote}
            >
                Submit Vote
            </Button>
        </VStack>
    );
}; 