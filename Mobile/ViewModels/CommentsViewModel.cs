using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PollSystem.Mobile.Models;
using PollSystem.Mobile.Services;

public partial class CommentsViewModel : BaseViewModel
{
    private readonly ICommentService _commentService;

    [ObservableProperty]
    private ObservableCollection<Comment> comments;

    [ObservableProperty]
    private string newCommentText;

    [ObservableProperty]
    private Comment selectedComment;

    public Guid PollId { get; private set; }

    public CommentsViewModel(ICommentService commentService)
    {
        _commentService = commentService;
        Comments = new ObservableCollection<Comment>();
    }

    public async Task InitializeAsync(Guid pollId)
    {
        PollId = pollId;
        await LoadCommentsAsync();
    }

    [RelayCommand]
    private async Task LoadCommentsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var comments = await _commentService.GetCommentsForPollAsync(PollId);
            Comments.Clear();
            foreach (var comment in comments.OrderByDescending(c => c.CreatedAt))
            {
                Comments.Add(comment);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddCommentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCommentText)) return;

        try
        {
            IsBusy = true;
            var comment = await _commentService.AddCommentAsync(
                PollId, 
                NewCommentText, 
                "Anonymous" // Replace with actual user name if available
            );

            if (comment != null)
            {
                Comments.Insert(0, comment);
                NewCommentText = string.Empty;
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", "Failed to add comment", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteCommentAsync(Comment comment)
    {
        try
        {
            var confirm = await Shell.Current.DisplayAlert(
                "Confirm Delete",
                "Are you sure you want to delete this comment?",
                "Yes",
                "No"
            );

            if (!confirm) return;

            IsBusy = true;
            var success = await _commentService.DeleteCommentAsync(comment.Id);
            if (success)
            {
                Comments.Remove(comment);
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", "Failed to delete comment", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }
} 