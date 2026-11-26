BEGIN TRANSACTION;
ALTER TABLE [Documents] ADD [IssuedBy] nvarchar(200) NULL;

CREATE INDEX [IX_Owners_FullName] ON [Owners] ([FullName]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108211129_AddDocumentIssuedByAndOwnerNameIndex', N'10.0.14');

COMMIT;
GO

