CREATE TABLE Users (
    UserId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    IpAddress NVARCHAR(45) NULL,
    IsAdmin BIT DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    LastActivityAt DATETIME2 DEFAULT GETUTCDATE()
);

CREATE INDEX IX_Users_IpAddress ON Users(IpAddress);

CREATE TABLE Polls (
    PollId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Title NVARCHAR(200) NOT NULL,
    CreatedBy UNIQUEIDENTIFIER NOT NULL,
    StartDate DATETIME2 NOT NULL,
    EndDate DATETIME2 NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),
    IsActive BIT DEFAULT 1,
    CONSTRAINT FK_Polls_Users FOREIGN KEY (CreatedBy) REFERENCES Users(UserId)
);

CREATE INDEX IX_Polls_CreatedBy ON Polls(CreatedBy);
CREATE INDEX IX_Polls_StartDate ON Polls(StartDate);
CREATE INDEX IX_Polls_EndDate ON Polls(EndDate) INCLUDE (IsActive);

CREATE TABLE PollOptions (
    OptionId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    PollId UNIQUEIDENTIFIER NOT NULL,
    OptionText NVARCHAR(200) NOT NULL,
    DisplayOrder INT NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_PollOptions_Polls FOREIGN KEY (PollId) REFERENCES Polls(PollId) ON DELETE CASCADE
);

CREATE INDEX IX_PollOptions_PollId ON PollOptions(PollId) INCLUDE (OptionText, DisplayOrder);

CREATE TABLE Votes (
    VoteId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    PollId UNIQUEIDENTIFIER NOT NULL,
    OptionId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    IpAddress NVARCHAR(45) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Votes_Polls FOREIGN KEY (PollId) REFERENCES Polls(PollId),
    CONSTRAINT FK_Votes_PollOptions FOREIGN KEY (OptionId) REFERENCES PollOptions(OptionId),
    CONSTRAINT FK_Votes_Users FOREIGN KEY (UserId) REFERENCES Users(UserId)
);

CREATE INDEX IX_Votes_PollId ON Votes(PollId);
CREATE INDEX IX_Votes_OptionId ON Votes(OptionId);
CREATE INDEX IX_Votes_UserId ON Votes(UserId);
CREATE INDEX IX_Votes_IpAddress ON Votes(IpAddress);

CREATE UNIQUE NONCLUSTERED INDEX UX_Votes_UserPoll 
ON Votes(PollId, UserId) 
WHERE UserId IS NOT NULL;

CREATE TABLE Comments (
    CommentId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    PollId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Content NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),
    IsDeleted BIT DEFAULT 0,
    CONSTRAINT FK_Comments_Polls FOREIGN KEY (PollId) REFERENCES Polls(PollId) ON DELETE CASCADE,
    CONSTRAINT FK_Comments_Users FOREIGN KEY (UserId) REFERENCES Users(UserId)
);

CREATE INDEX IX_Comments_PollId ON Comments(PollId) INCLUDE (CreatedAt);
CREATE INDEX IX_Comments_UserId ON Comments(UserId);

CREATE PROCEDURE [dbo].[GetPollResults]
    @PollId UNIQUEIDENTIFIER
AS
BEGIN
    SELECT 
        p.PollId,
        p.Title,
        p.StartDate,
        p.EndDate,
        p.IsActive,
        po.OptionId,
        po.OptionText,
        COUNT(v.VoteId) as VoteCount
    FROM Polls p
    INNER JOIN PollOptions po ON p.PollId = po.PollId
    LEFT JOIN Votes v ON po.OptionId = v.OptionId
    WHERE p.PollId = @PollId
    GROUP BY 
        p.PollId,
        p.Title,
        p.StartDate,
        p.EndDate,
        p.IsActive,
        po.OptionId,
        po.OptionText
    ORDER BY po.DisplayOrder;
END;

CREATE PROCEDURE [dbo].[HasUserVoted]
    @PollId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SELECT CASE 
        WHEN EXISTS (
            SELECT 1 FROM Votes 
            WHERE PollId = @PollId AND UserId = @UserId
        ) THEN 1
        ELSE 0
    END;
END;

CREATE TRIGGER TR_Polls_UpdateTimestamp
ON Polls
AFTER UPDATE
AS
BEGIN
    UPDATE Polls
    SET UpdatedAt = GETUTCDATE()
    FROM Polls p
    INNER JOIN inserted i ON p.PollId = i.PollId;
END; 