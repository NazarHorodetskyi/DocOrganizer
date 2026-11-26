IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [DocumentTypes] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [DefaultReminderDays] int NOT NULL,
    CONSTRAINT [PK_DocumentTypes] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_DocumentTypes_DefaultReminderDays] CHECK ([DefaultReminderDays] BETWEEN 1 AND 365)
);

CREATE TABLE [Owners] (
    [Id] int NOT NULL IDENTITY,
    [FullName] nvarchar(150) NOT NULL,
    [Email] nvarchar(256) NOT NULL,
    [BirthDate] date NULL,
    CONSTRAINT [PK_Owners] PRIMARY KEY ([Id])
);

CREATE TABLE [Tags] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(50) NOT NULL,
    CONSTRAINT [PK_Tags] PRIMARY KEY ([Id])
);

CREATE TABLE [Documents] (
    [Id] int NOT NULL IDENTITY,
    [OwnerId] int NOT NULL,
    [DocumentTypeId] int NOT NULL,
    [Number] nvarchar(50) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [IssueDate] date NOT NULL,
    [ExpiryDate] date NULL,
    [FilePath] nvarchar(500) NULL,
    [Notes] nvarchar(1000) NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [Version] rowversion NOT NULL,
    CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Documents_ExpiryAfterIssue] CHECK ([ExpiryDate] IS NULL OR [ExpiryDate] >= [IssueDate]),
    CONSTRAINT [FK_Documents_DocumentTypes_DocumentTypeId] FOREIGN KEY ([DocumentTypeId]) REFERENCES [DocumentTypes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Documents_Owners_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Owners] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [DocumentTags] (
    [DocumentId] int NOT NULL,
    [TagId] int NOT NULL,
    [AddedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_DocumentTags] PRIMARY KEY ([DocumentId], [TagId]),
    CONSTRAINT [FK_DocumentTags_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DocumentTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Reminders] (
    [Id] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [DaysBefore] int NOT NULL,
    [RemindOn] date NOT NULL,
    [Channel] nvarchar(10) NOT NULL,
    [IsSent] bit NOT NULL,
    [SentAt] datetime2 NULL,
    CONSTRAINT [PK_Reminders] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Reminders_DaysBefore] CHECK ([DaysBefore] BETWEEN 0 AND 365),
    CONSTRAINT [FK_Reminders_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'DefaultReminderDays', N'Name') AND [object_id] = OBJECT_ID(N'[DocumentTypes]'))
    SET IDENTITY_INSERT [DocumentTypes] ON;
INSERT INTO [DocumentTypes] ([Id], [DefaultReminderDays], [Name])
VALUES (1, 60, N'ID-картка'),
(2, 90, N'Закордонний паспорт'),
(3, 30, N'Віза'),
(4, 30, N'Посвідчення водія'),
(5, 14, N'Медичне страхування'),
(6, 14, N'Автоцивілка (ОСЦПВ)'),
(7, 30, N'Сертифікат'),
(8, 30, N'Договір');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'DefaultReminderDays', N'Name') AND [object_id] = OBJECT_ID(N'[DocumentTypes]'))
    SET IDENTITY_INSERT [DocumentTypes] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Tags]'))
    SET IDENTITY_INSERT [Tags] ON;
INSERT INTO [Tags] ([Id], [Name])
VALUES (1, N'Особисте'),
(2, N'Подорожі'),
(3, N'Авто'),
(4, N'Робота'),
(5, N'Фінанси'),
(6, N'Терміново');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Tags]'))
    SET IDENTITY_INSERT [Tags] OFF;

CREATE UNIQUE INDEX [IX_Documents_DocumentTypeId_Number] ON [Documents] ([DocumentTypeId], [Number]);

CREATE INDEX [IX_Documents_ExpiryDate] ON [Documents] ([ExpiryDate]);

CREATE INDEX [IX_Documents_OwnerId_Status] ON [Documents] ([OwnerId], [Status]);

CREATE INDEX [IX_DocumentTags_TagId] ON [DocumentTags] ([TagId]);

CREATE UNIQUE INDEX [IX_DocumentTypes_Name] ON [DocumentTypes] ([Name]);

CREATE UNIQUE INDEX [IX_Owners_Email] ON [Owners] ([Email]);

CREATE UNIQUE INDEX [IX_Reminders_DocumentId_DaysBefore] ON [Reminders] ([DocumentId], [DaysBefore]);

CREATE INDEX [IX_Reminders_IsSent_RemindOn] ON [Reminders] ([IsSent], [RemindOn]);

CREATE UNIQUE INDEX [IX_Tags_Name] ON [Tags] ([Name]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108204603_InitialCreate', N'10.0.14');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Documents] ADD [IssuedBy] nvarchar(200) NULL;

CREATE INDEX [IX_Owners_FullName] ON [Owners] ([FullName]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108211129_AddDocumentIssuedByAndOwnerNameIndex', N'10.0.14');

COMMIT;
GO

