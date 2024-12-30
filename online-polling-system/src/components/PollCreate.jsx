import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';

const PollCreate = () => {
    const navigate = useNavigate();
    const [formData, setFormData] = useState({
        title: '',
        options: ['', ''], // Start with 2 empty options
        startDate: new Date().toISOString().split('T')[0],
        endDate: ''
    });

    const handleAddOption = () => {
        setFormData(prev => ({
            ...prev,
            options: [...prev.options, '']
        }));
    };

    const handleOptionChange = (index, value) => {
        const newOptions = [...formData.options];
        newOptions[index] = value;
        setFormData(prev => ({
            ...prev,
            options: newOptions
        }));
    };

    const handleSubmit = async (e) => {
        e.preventDefault();
        try {
            const response = await axios.post('/api/poll', formData);
            navigate(`/poll/${response.data.pollId}`);
        } catch (error) {
            console.error('Error creating poll:', error);
        }
    };

    return (
        <div className="max-w-2xl mx-auto p-4">
            <h2 className="text-2xl font-bold mb-4">Create New Poll</h2>
            <form onSubmit={handleSubmit} className="space-y-4">
                <div>
                    <label className="block mb-2">Poll Question</label>
                    <input
                        type="text"
                        value={formData.title}
                        onChange={(e) => setFormData(prev => ({ ...prev, title: e.target.value }))}
                        className="w-full p-2 border rounded"
                        required
                    />
                </div>

                <div className="space-y-2">
                    <label className="block mb-2">Options</label>
                    {formData.options.map((option, index) => (
                        <input
                            key={index}
                            type="text"
                            value={option}
                            onChange={(e) => handleOptionChange(index, e.target.value)}
                            className="w-full p-2 border rounded mb-2"
                            placeholder={`Option ${index + 1}`}
                            required
                        />
                    ))}
                    <button
                        type="button"
                        onClick={handleAddOption}
                        className="bg-gray-200 px-4 py-2 rounded"
                    >
                        Add Option
                    </button>
                </div>

                <div className="grid grid-cols-2 gap-4">
                    <div>
                        <label className="block mb-2">Start Date</label>
                        <input
                            type="date"
                            value={formData.startDate}
                            onChange={(e) => setFormData(prev => ({ ...prev, startDate: e.target.value }))}
                            className="w-full p-2 border rounded"
                            required
                        />
                    </div>
                    <div>
                        <label className="block mb-2">End Date (Optional)</label>
                        <input
                            type="date"
                            value={formData.endDate}
                            onChange={(e) => setFormData(prev => ({ ...prev, endDate: e.target.value }))}
                            className="w-full p-2 border rounded"
                        />
                    </div>
                </div>

                <button
                    type="submit"
                    className="bg-blue-500 text-white px-6 py-2 rounded hover:bg-blue-600"
                >
                    Create Poll
                </button>
            </form>
        </div>
    );
};

export default PollCreate; 