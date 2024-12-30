import React from 'react';
import { Bar } from 'react-chartjs-2';
import {
    Chart as ChartJS,
    CategoryScale,
    LinearScale,
    BarElement,
    Title,
    Tooltip,
    Legend
} from 'chart.js';

ChartJS.register(
    CategoryScale,
    LinearScale,
    BarElement,
    Title,
    Tooltip,
    Legend
);

const PollResults = ({ poll }) => {
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
                position: 'top',
            },
            title: {
                display: true,
                text: 'Poll Results',
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

    const totalVotes = poll.options.reduce((sum, option) => sum + option.voteCount, 0);

    return (
        <div className="space-y-6">
            <div className="bg-white p-6 rounded-lg shadow">
                <Bar data={data} options={options} />
            </div>

            <div className="space-y-4">
                {poll.options.map(option => (
                    <div key={option.optionId} className="flex justify-between items-center">
                        <span>{option.optionText}</span>
                        <span className="font-bold">
                            {option.voteCount} votes
                            ({totalVotes > 0 
                                ? `${((option.voteCount / totalVotes) * 100).toFixed(1)}%` 
                                : '0%'})
                        </span>
                    </div>
                ))}
                <div className="text-right text-gray-600">
                    Total votes: {totalVotes}
                </div>
            </div>
        </div>
    );
};

export default PollResults; 