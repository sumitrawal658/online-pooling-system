import { Box } from '@chakra-ui/react';
import {
    Chart as ChartJS,
    CategoryScale,
    LinearScale,
    BarElement,
    Title,
    Tooltip,
    Legend
} from 'chart.js';
import { Bar } from 'react-chartjs-2';
import { Poll } from '../types';

ChartJS.register(
    CategoryScale,
    LinearScale,
    BarElement,
    Title,
    Tooltip,
    Legend
);

interface PollResultsProps {
    poll: Poll;
}

export const PollResults = ({ poll }: PollResultsProps) => {
    const data = {
        labels: poll.options.map(option => option.optionText),
        datasets: [
            {
                label: 'Votes',
                data: poll.options.map(option => option.voteCount),
                backgroundColor: 'rgba(54, 162, 235, 0.5)',
                borderColor: 'rgba(54, 162, 235, 1)',
                borderWidth: 1,
            },
        ],
    };

    const options = {
        responsive: true,
        plugins: {
            legend: {
                position: 'top' as const,
            },
            title: {
                display: true,
                text: poll.title,
            },
        },
        scales: {
            y: {
                beginAtZero: true,
                ticks: {
                    stepSize: 1
                }
            }
        }
    };

    return (
        <Box p={4}>
            <Bar data={data} options={options} />
        </Box>
    );
}; 