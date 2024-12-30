using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace PollSystem.Mobile.ViewModels
{
    public partial class PollResultsViewModel : BaseViewModel
    {
        [ObservableProperty]
        private ISeries[] chartSeries;

        [ObservableProperty]
        private string title;

        public void UpdateResults(Poll poll)
        {
            if (poll == null) return;

            Title = poll.Title;
            var totalVotes = poll.Options.Sum(o => o.VoteCount);

            var series = poll.Options.Select(option => new PieSeries<double>
            {
                Values = new[] { option.VoteCount },
                Name = $"{option.OptionText} ({option.VoteCount} votes)",
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsFormatter = point => $"{point.PrimaryValue:N0} ({point.PrimaryValue / totalVotes:P1})"
            }).ToArray();

            ChartSeries = series;
        }
    }
} 