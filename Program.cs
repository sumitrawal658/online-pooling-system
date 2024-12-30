// Add SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<GroupManager>();

// Add exception handling
builder.Services.AddExceptionHandling();

// ... existing code ...

// Configure SignalR endpoint
app.MapHub<PollHub>("/pollHub"); 

// Configure middleware
app.UseGlobalExceptionHandler(); 