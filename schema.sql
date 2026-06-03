CREATE TABLE [AiUsages] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [UsageDate] datetime2 NOT NULL,
    [PromptsCount] int NOT NULL,
    [TokensUsed] int NOT NULL,
    CONSTRAINT [PK_AiUsages] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Payments] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(max) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Provider] nvarchar(max) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [TransactionId] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Payments] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Places] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [About] nvarchar(max) NULL,
    [Address] nvarchar(max) NULL,
    [Latitude] decimal(9,6) NOT NULL,
    [Longitude] decimal(9,6) NOT NULL,
    [MinPrice] decimal(12,2) NULL,
    [MaxPrice] decimal(12,2) NULL,
    [Phone] nvarchar(max) NULL,
    [WebsiteUrl] nvarchar(max) NULL,
    [Category] nvarchar(max) NULL,
    [CreatedByUserId] nvarchar(max) NULL,
    [Visibility] nvarchar(max) NOT NULL,
    [ShareToken] nvarchar(max) NULL,
    [IsApproved] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Places] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Plans] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(max) NOT NULL,
    [Title] nvarchar(max) NOT NULL,
    [StartDate] datetime2 NULL,
    [EndDate] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Plans] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Tags] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Tags] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [UserLists] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(max) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_UserLists] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [UserProfiles] (
    [UserId] nvarchar(450) NOT NULL,
    [DisplayName] nvarchar(max) NULL,
    [AvatarUrl] nvarchar(max) NULL,
    [Tier] nvarchar(max) NOT NULL,
    [ProExpiresAt] datetime2 NULL,
    [MaxPlaces] int NOT NULL,
    [MaxPlans] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_UserProfiles] PRIMARY KEY ([UserId])
);
GO


CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Events] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [PlaceId] int NULL,
    [BannerImageUrl] nvarchar(max) NULL,
    [StartAt] datetime2 NOT NULL,
    [EndAt] datetime2 NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsFeatured] bit NOT NULL,
    [CreatedByUserId] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Events] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Events_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id])
);
GO


CREATE TABLE [PlaceAttributes] (
    [Id] int NOT NULL IDENTITY,
    [PlaceId] int NOT NULL,
    [GroupName] nvarchar(max) NOT NULL,
    [Label] nvarchar(max) NOT NULL,
    [IsAvailable] bit NOT NULL,
    CONSTRAINT [PK_PlaceAttributes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PlaceAttributes_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [PlaceImages] (
    [Id] int NOT NULL IDENTITY,
    [PlaceId] int NOT NULL,
    [Url] nvarchar(max) NOT NULL,
    [IsPrimary] bit NOT NULL,
    [UploadedByUserId] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PlaceImages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PlaceImages_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [PlaceReviews] (
    [Id] int NOT NULL IDENTITY,
    [PlaceId] int NOT NULL,
    [UserId] nvarchar(max) NOT NULL,
    [QualityRating] tinyint NOT NULL,
    [ServiceRating] tinyint NOT NULL,
    [FoodReview] nvarchar(max) NULL,
    [StaffReview] nvarchar(max) NULL,
    [Content] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PlaceReviews] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PlaceReviews_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [PlanItems] (
    [Id] int NOT NULL IDENTITY,
    [PlanId] int NOT NULL,
    [PlaceId] int NOT NULL,
    [VisitTime] datetime2 NULL,
    [OrderIndex] int NOT NULL,
    [Note] nvarchar(max) NULL,
    CONSTRAINT [PK_PlanItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PlanItems_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PlanItems_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Plans] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [PlaceTags] (
    [PlaceId] int NOT NULL,
    [TagId] int NOT NULL,
    CONSTRAINT [PK_PlaceTags] PRIMARY KEY ([PlaceId], [TagId]),
    CONSTRAINT [FK_PlaceTags_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PlaceTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [UserListItems] (
    [ListId] int NOT NULL,
    [PlaceId] int NOT NULL,
    [AddedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_UserListItems] PRIMARY KEY ([ListId], [PlaceId]),
    CONSTRAINT [FK_UserListItems_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UserListItems_UserLists_ListId] FOREIGN KEY ([ListId]) REFERENCES [UserLists] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [FeedPosts] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(max) NOT NULL,
    [Summary] nvarchar(max) NULL,
    [CoverImageUrl] nvarchar(max) NULL,
    [Type] nvarchar(max) NOT NULL,
    [EventId] int NULL,
    [PublishedAt] datetime2 NOT NULL,
    [IsPinned] bit NOT NULL,
    CONSTRAINT [PK_FeedPosts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FeedPosts_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id])
);
GO


CREATE UNIQUE INDEX [IX_AiUsages_UserId_UsageDate] ON [AiUsages] ([UserId], [UsageDate]);
GO


CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
GO


CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO


CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
GO


CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
GO


CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
GO


CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
GO


CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO


CREATE INDEX [IX_Events_PlaceId] ON [Events] ([PlaceId]);
GO


CREATE INDEX [IX_FeedPosts_EventId] ON [FeedPosts] ([EventId]);
GO


CREATE INDEX [IX_PlaceAttributes_PlaceId] ON [PlaceAttributes] ([PlaceId]);
GO


CREATE INDEX [IX_PlaceImages_PlaceId] ON [PlaceImages] ([PlaceId]);
GO


CREATE INDEX [IX_PlaceReviews_PlaceId] ON [PlaceReviews] ([PlaceId]);
GO


CREATE INDEX [IX_PlaceTags_TagId] ON [PlaceTags] ([TagId]);
GO


CREATE INDEX [IX_PlanItems_PlaceId] ON [PlanItems] ([PlaceId]);
GO


CREATE INDEX [IX_PlanItems_PlanId] ON [PlanItems] ([PlanId]);
GO


CREATE INDEX [IX_UserListItems_PlaceId] ON [UserListItems] ([PlaceId]);
GO


