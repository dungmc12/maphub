-- ==============================================================================
-- CityScout Database Schema (SQL Server)
-- Chạy lệnh này một lần để tạo database + seed data Hà Nội
-- ==============================================================================
USE master;
GO
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'CityScout')
    CREATE DATABASE CityScout;
GO
USE CityScout;
GO

-- ==============================================================================
-- 1. USER PROFILES
-- ==============================================================================
CREATE TABLE dbo.UserProfiles (
    UserId       NVARCHAR(450) NOT NULL PRIMARY KEY,
    DisplayName  NVARCHAR(120) NULL,
    AvatarUrl    NVARCHAR(400) NULL,
    Tier         NVARCHAR(20)  NOT NULL DEFAULT 'free',   -- free | pro | admin
    ProExpiresAt DATETIME2     NULL,
    MaxPlaces    INT           NOT NULL DEFAULT 3,        -- Free: tối đa 3 địa điểm
    MaxPlans     INT           NOT NULL DEFAULT 3,
    CreatedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

-- ==============================================================================
-- 2. PAYMENTS & AI USAGE
-- ==============================================================================
CREATE TABLE dbo.Payments (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    UserId        NVARCHAR(450) NOT NULL,
    Amount        DECIMAL(18,2) NOT NULL,
    Provider      NVARCHAR(50)  NOT NULL,
    Status        NVARCHAR(20)  NOT NULL DEFAULT 'pending',  -- pending|completed|failed
    TransactionId NVARCHAR(100) NULL,
    Description   NVARCHAR(255) NULL,
    CreatedAt     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.AiUsage (
    Id           INT           IDENTITY(1,1) PRIMARY KEY,
    UserId       NVARCHAR(450) NOT NULL,
    UsageDate    DATE          NOT NULL,
    PromptsCount INT           NOT NULL DEFAULT 0,
    TokensUsed   INT           NOT NULL DEFAULT 0,
    CONSTRAINT UC_AiUsage_User_Date UNIQUE (UserId, UsageDate)
);

-- ==============================================================================
-- 3. ĐỊA ĐIỂM (PLACES)
-- ==============================================================================
CREATE TABLE dbo.Places (
    Id              INT           IDENTITY(1,1) PRIMARY KEY,
    Name            NVARCHAR(160) NOT NULL,
    Description     NVARCHAR(500) NULL,
    About           NVARCHAR(MAX) NULL,
    Address         NVARCHAR(220) NULL,
    Latitude        DECIMAL(9,6)  NOT NULL,
    Longitude       DECIMAL(9,6)  NOT NULL,
    MinPrice        DECIMAL(12,2) NULL,
    MaxPrice        DECIMAL(12,2) NULL,
    Phone           NVARCHAR(40)  NULL,
    WebsiteUrl      NVARCHAR(400) NULL,
    Category        NVARCHAR(80)  NULL,
    CreatedByUserId NVARCHAR(450) NULL,
    Visibility      NVARCHAR(20)  NOT NULL DEFAULT 'private',  -- private | public
    ShareToken      NVARCHAR(64)  NULL,
    IsApproved      BIT           NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Places_Visibility_Approved ON dbo.Places(Visibility, IsApproved);
CREATE INDEX IX_Places_CreatedBy ON dbo.Places(CreatedByUserId);

-- ==============================================================================
-- 4. ẢNH ĐỊA ĐIỂM
-- ==============================================================================
CREATE TABLE dbo.PlaceImages (
    Id               INT           IDENTITY(1,1) PRIMARY KEY,
    PlaceId          INT           NOT NULL,
    Url              NVARCHAR(400) NOT NULL,
    IsPrimary        BIT           NOT NULL DEFAULT 0,
    Caption          NVARCHAR(200) NULL,
    UploadedByUserId NVARCHAR(450) NULL,
    CreatedAt        DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_PlaceImages_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id) ON DELETE CASCADE
);

-- ==============================================================================
-- 5. THUỘC TÍNH ĐỊA ĐIỂM (như Google Maps: Tiện ích, Tùy chọn dịch vụ...)
-- ==============================================================================
CREATE TABLE dbo.PlaceAttributes (
    Id          INT          IDENTITY(1,1) PRIMARY KEY,
    PlaceId     INT          NOT NULL,
    GroupName   NVARCHAR(80) NOT NULL,   -- VD: "Tùy chọn dịch vụ", "Tiện ích", "Khả năng tiếp cận"
    Label       NVARCHAR(80) NOT NULL,   -- VD: "Có chỗ đỗ xe", "Wifi miễn phí"
    IsAvailable BIT          NOT NULL DEFAULT 1,
    CONSTRAINT FK_PlaceAttributes_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id) ON DELETE CASCADE
);
CREATE INDEX IX_PlaceAttributes_PlaceId ON dbo.PlaceAttributes(PlaceId);

-- ==============================================================================
-- 6. TAGS
-- ==============================================================================
CREATE TABLE dbo.Tags (
    Id   INT          IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(60) NOT NULL UNIQUE
);

CREATE TABLE dbo.PlaceTags (
    PlaceId INT NOT NULL,
    TagId   INT NOT NULL,
    PRIMARY KEY (PlaceId, TagId),
    CONSTRAINT FK_PlaceTags_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id) ON DELETE CASCADE,
    CONSTRAINT FK_PlaceTags_Tags   FOREIGN KEY (TagId)   REFERENCES dbo.Tags(Id)   ON DELETE CASCADE
);

-- ==============================================================================
-- 7. ĐÁNH GIÁ (REVIEWS)
-- ==============================================================================
CREATE TABLE dbo.PlaceReviews (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    PlaceId       INT           NOT NULL,
    UserId        NVARCHAR(450) NOT NULL,
    QualityRating TINYINT       NOT NULL CHECK (QualityRating BETWEEN 1 AND 5),  -- Chất lượng tổng thể
    ServiceRating TINYINT       NOT NULL CHECK (ServiceRating BETWEEN 1 AND 5),  -- Nhân viên phục vụ
    FoodRating    TINYINT       NULL     CHECK (FoodRating    BETWEEN 1 AND 5),  -- Đồ ăn (cho nhà hàng/cafe)
    FoodReview    NVARCHAR(400) NULL,
    StaffReview   NVARCHAR(400) NULL,
    Content       NVARCHAR(MAX) NULL,
    CreatedAt     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_PlaceReviews_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id) ON DELETE CASCADE
);

-- ==============================================================================
-- 8. SỰ KIỆN
-- ==============================================================================
CREATE TABLE dbo.Events (
    Id              INT           IDENTITY(1,1) PRIMARY KEY,
    Title           NVARCHAR(160) NOT NULL,
    Description     NVARCHAR(800) NULL,
    PlaceId         INT           NULL,
    BannerImageUrl  NVARCHAR(400) NULL,
    StartAt         DATETIME2     NOT NULL,
    EndAt           DATETIME2     NULL,
    Status          NVARCHAR(20)  NOT NULL DEFAULT 'upcoming',  -- upcoming|ongoing|ended
    IsFeatured      BIT           NOT NULL DEFAULT 0,
    CreatedByUserId NVARCHAR(450) NULL,
    CreatedAt       DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Events_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id) ON DELETE SET NULL
);

-- ==============================================================================
-- 9. TIN TỨC / NEWSFEED
-- ==============================================================================
CREATE TABLE dbo.FeedPosts (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    Title         NVARCHAR(160) NOT NULL,
    Summary       NVARCHAR(300) NULL,
    CoverImageUrl NVARCHAR(400) NULL,
    Type          NVARCHAR(20)  NOT NULL DEFAULT 'news',  -- news | guide | tip
    EventId       INT           NULL,
    PublishedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    IsPinned      BIT           NOT NULL DEFAULT 0,
    CONSTRAINT FK_FeedPosts_Events FOREIGN KEY (EventId) REFERENCES dbo.Events(Id) ON DELETE SET NULL
);

-- ==============================================================================
-- 10. DANH SÁCH & KẾ HOẠCH
-- ==============================================================================
CREATE TABLE dbo.UserLists (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    UserId      NVARCHAR(450) NOT NULL,
    Name        NVARCHAR(120) NOT NULL,
    Description NVARCHAR(300) NULL,
    CreatedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.UserListItems (
    ListId   INT       NOT NULL,
    PlaceId  INT       NOT NULL,
    AddedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (ListId, PlaceId),
    CONSTRAINT FK_UserListItems_Lists  FOREIGN KEY (ListId)  REFERENCES dbo.UserLists(Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserListItems_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id)    ON DELETE CASCADE
);

CREATE TABLE dbo.Plans (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    UserId      NVARCHAR(450) NOT NULL,
    Title       NVARCHAR(160) NOT NULL,
    StartDate   DATE          NULL,
    EndDate     DATE          NULL,
    ShareToken  NVARCHAR(64)  NULL,   -- null = chưa chia sẻ, có giá trị = link công khai
    IsPublic    BIT           NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.PlanItems (
    Id         INT           IDENTITY(1,1) PRIMARY KEY,
    PlanId     INT           NOT NULL,
    PlaceId    INT           NOT NULL,
    VisitTime  DATETIME2     NULL,
    OrderIndex INT           NOT NULL DEFAULT 0,
    Note       NVARCHAR(300) NULL,
    CONSTRAINT FK_PlanItems_Plans  FOREIGN KEY (PlanId)  REFERENCES dbo.Plans(Id)  ON DELETE CASCADE,
    CONSTRAINT FK_PlanItems_Places FOREIGN KEY (PlaceId) REFERENCES dbo.Places(Id) ON DELETE CASCADE
);

-- ==============================================================================
-- SEED DATA: Tags
-- ==============================================================================
INSERT INTO dbo.Tags (Name) VALUES
(N'Đi bộ'), (N'Gia đình'), (N'Cặp đôi'), (N'Nhóm bạn'), (N'Chụp ảnh'),
(N'Dã ngoại'), (N'Ăn uống'), (N'Cà phê'), (N'Lịch sử'), (N'Thiên nhiên'),
(N'Mua sắm'), (N'Nightlife'), (N'Thể thao'), (N'Âm nhạc'), (N'Sinh viên');
GO

-- ==============================================================================
-- SEED DATA: Địa điểm Hà Nội → Láng → Hòa Lạc
-- ==============================================================================
SET IDENTITY_INSERT dbo.Places ON;

INSERT INTO dbo.Places (Id, Name, Description, About, Address, Latitude, Longitude, Category, Visibility, IsApproved, MinPrice, MaxPrice, Phone, WebsiteUrl) VALUES
(1, N'Hồ Hoàn Kiếm',
    N'Trái tim xanh của Hà Nội với tháp Rùa và đền Ngọc Sơn huyền thoại.',
    N'Không gian đi bộ cuối tuần yêu thích của người Hà Nội. Bờ hồ thoáng mát với nhiều ghế đá, phù hợp đi dạo buổi sáng sớm hoặc tối mát. Xung quanh có nhiều hàng quán, trà đá vỉa hè đặc trưng.',
    N'Hoàn Kiếm, Hà Nội', 21.028511, 105.852014, N'Tham quan', 'public', 1, 0, 0, NULL, NULL),

(2, N'Văn Miếu - Quốc Tử Giám',
    N'Trường đại học đầu tiên của Việt Nam, biểu tượng văn hiến ngàn năm.',
    N'Di tích lịch sử văn hóa cấp quốc gia đặc biệt. Khu vực có 5 sân (ngũ đình), 82 bia tiến sĩ từ thế kỷ XV. Có hướng dẫn viên tiếng Việt và tiếng Anh. Nên ghé vào buổi sáng để tránh đông.',
    N'58 Quốc Tử Giám, Đống Đa, Hà Nội', 21.027763, 105.835552, N'Văn hóa', 'public', 1, 30000, 70000, N'024 3845 2917', NULL),

(3, N'Chùa Láng (Chiêu Thiền Tự)',
    N'Ngôi chùa cổ kính hơn 800 năm tuổi, không gian yên bình giữa lòng thành phố.',
    N'Chùa nằm giữa khu dân cư phố Chùa Láng, có nhiều cây cổ thụ bóng mát. Thường được người dân địa phương ghé lễ vào sáng sớm. Phù hợp tham quan tự do, không thu phí vào cổng.',
    N'Phố Chùa Láng, Đống Đa, Hà Nội', 21.021144, 105.817195, N'Tâm linh', 'public', 1, 0, 0, NULL, NULL),

(4, N'Hồ Tây',
    N'Hồ nước ngọt tự nhiên lớn nhất Hà Nội, nơi hẹn hò lý tưởng buổi chiều tà.',
    N'Chu vi hơn 17km, xung quanh có đường dạo bộ và xe đạp. Bờ hồ phía Nam (Trúc Bạch) có nhiều quán cafe view hồ đẹp. Chiều tối hoàng hôn rực rỡ, lý tưởng để chụp ảnh.',
    N'Tây Hồ, Hà Nội', 21.059088, 105.823047, N'Thư giãn', 'public', 1, 0, 0, NULL, NULL),

(5, N'Nhà hàng Bún Chả Hương Liên',
    N'Quán bún chả nổi tiếng thế giới, nơi Tổng thống Obama từng ghé thăm năm 2016.',
    N'Quán bình dân nhưng nổi tiếng toàn cầu nhờ chuyến thăm của Obama và Anthony Bourdain. Bún chả và nem rán là món must-try. Thường đông vào giờ trưa nên nên đến trước 11h30 hoặc sau 13h30.',
    N'24 Lê Văn Hưu, Hai Bà Trưng, Hà Nội', 21.021667, 105.852500, N'Nhà hàng', 'public', 1, 40000, 80000, N'024 3943 4106', NULL),

(6, N'Cà phê Giảng (Egg Coffee)',
    N'Nơi phát minh ra cà phê trứng - đặc sản nổi tiếng nhất của Hà Nội.',
    N'Quán nhỏ, leo cầu thang lên tầng 2 sẽ có view nhìn xuống phố cổ. Cà phê trứng phải thưởng thức nóng. Quán mở từ 7h sáng, thường đông khách Tây vào buổi chiều.',
    N'39 Nguyễn Hữu Huân, Hoàn Kiếm, Hà Nội', 21.035600, 105.851200, N'Cà phê', 'public', 1, 25000, 55000, NULL, NULL),

(7, N'Làng Văn hóa Du lịch các Dân tộc Việt Nam',
    N'Không gian dã ngoại rộng lớn với kiến trúc 54 dân tộc, phù hợp cả gia đình.',
    N'Khu du lịch rộng hàng trăm ha tại Đồng Mô. Có khu cắm trại, BBQ, thuyền kayak trên hồ. Cuối tuần thường có biểu diễn văn hóa dân gian. Cách trung tâm Hà Nội khoảng 40km về phía Tây.',
    N'Đồng Mô, Sơn Tây, Hà Nội', 21.038700, 105.394000, N'Vui chơi', 'public', 1, 30000, 150000, N'024 3368 3684', NULL),

(8, N'Đại học FPT Hà Nội',
    N'Khuôn viên đại học hiện đại bậc nhất Việt Nam, kiến trúc ấn tượng tại Hòa Lạc.',
    N'Trường đại học với kiến trúc độc đáo do KTS Võ Trọng Nghĩa thiết kế. Khuôn viên rộng 30ha với nhiều sân chơi, khu ký túc xá, trung tâm thể thao. Khu vực xung quanh có nhiều quán ăn bình dân cho sinh viên.',
    N'Khu CNC Hòa Lạc, km29 Đại lộ Thăng Long, Thạch Thất, Hà Nội', 21.012789, 105.525678, N'Giáo dục', 'public', 1, 0, 0, N'024 7300 1866', N'https://hanoi.fpt.edu.vn'),

(9, N'Khu Công nghệ cao Hòa Lạc',
    N'Trung tâm R&D và công nghệ lớn nhất Việt Nam, nơi quy tụ các tập đoàn FPT, Viettel, Samsung...',
    N'Khu công nghệ cao rộng 1.586ha. Phù hợp tham quan các tòa nhà hiện đại và tìm hiểu về hệ sinh thái công nghệ Việt Nam. Có thể đi xe máy hoặc ô tô từ Hà Nội theo Đại lộ Thăng Long.',
    N'Km29 Đại lộ Thăng Long, Thạch Thất, Hà Nội', 21.007410, 105.525229, N'Công nghệ', 'public', 1, 0, 0, NULL, NULL),

(10, N'Nhà hàng Gà Đồi Hòa Lạc',
    N'Gà đồi thả vườn đặc sản vùng Ba Vì, không gian thoáng mát giữa thiên nhiên.',
    N'Nhà hàng chuyên gà đồi, dê núi và các đặc sản vùng núi phía Tây Hà Nội. Phong cách nhà hàng vườn, có bãi đỗ xe rộng. Đặt bàn trước vào cuối tuần vì thường kín chỗ.',
    N'Quốc lộ 21, Thạch Thất, Hà Nội', 21.010500, 105.540000, N'Nhà hàng', 'public', 1, 150000, 500000, N'0912 345 678', NULL),

(11, N'Cà phê The Coffee House - Láng Hạ',
    N'Không gian làm việc thoải mái, WiFi tốt, gần khu văn phòng Láng Hạ.',
    N'Chi nhánh The Coffee House rộng rãi, tầng 2 có view nhìn ra phố. WiFi ổn định, phù hợp ngồi làm việc cả ngày. Giờ cao điểm (8-10h, 13-14h) thường đông.',
    N'187 Láng Hạ, Đống Đa, Hà Nội', 21.019800, 105.820400, N'Cà phê', 'public', 1, 39000, 85000, NULL, NULL),

(12, N'Công viên Thủ Lệ (Vườn thú Hà Nội)',
    N'Vườn thú kết hợp công viên cây xanh, điểm vui chơi quen thuộc cho gia đình Hà Nội.',
    N'Khu vui chơi rộng 35ha với nhiều loài động vật. Phù hợp cho gia đình có con nhỏ. Bên trong có hồ nước, cầu trượt, khu vực picnic dưới bóng cây. Giá vé hợp lý.',
    N'Kim Mã, Ba Đình, Hà Nội', 21.033900, 105.831500, N'Vui chơi', 'public', 1, 30000, 60000, NULL, NULL);

SET IDENTITY_INSERT dbo.Places OFF;
GO

-- ==============================================================================
-- SEED DATA: Ảnh địa điểm
-- ==============================================================================
INSERT INTO dbo.PlaceImages (PlaceId, Url, IsPrimary, Caption) VALUES
(1,  'https://picsum.photos/seed/hoguom1/800/500',   1, N'Hồ Hoàn Kiếm nhìn từ bờ Đinh Tiên Hoàng'),
(1,  'https://picsum.photos/seed/hoguom2/800/500',   0, N'Tháp Rùa lúc hoàng hôn'),
(2,  'https://picsum.photos/seed/vanmieu1/800/500',  1, N'Cổng Văn Miếu Môn'),
(2,  'https://picsum.photos/seed/vanmieu2/800/500',  0, N'Khuê Văn Các'),
(3,  'https://picsum.photos/seed/chualang1/800/500', 1, N'Chùa Láng cổng chính'),
(4,  'https://picsum.photos/seed/hotay1/800/500',    1, N'Hồ Tây lúc chiều tà'),
(5,  'https://picsum.photos/seed/buncha1/800/500',   1, N'Bún chả Hương Liên'),
(5,  'https://picsum.photos/seed/buncha2/800/500',   0, N'Không gian quán'),
(6,  'https://picsum.photos/seed/cafegiang1/800/500',1, N'Cà phê trứng đặc trưng'),
(7,  'https://picsum.photos/seed/langvanhoa1/800/500',1,N'Toàn cảnh Làng Văn hóa'),
(7,  'https://picsum.photos/seed/langvanhoa2/800/500',0,N'Khu hồ Đồng Mô'),
(8,  'https://picsum.photos/seed/fpthn1/800/500',    1, N'Khuôn viên ĐH FPT Hà Nội'),
(8,  'https://picsum.photos/seed/fpthn2/800/500',    0, N'Tòa nhà hiện đại ĐH FPT'),
(9,  'https://picsum.photos/seed/hoalac1/800/500',   1, N'Khu CNC Hòa Lạc'),
(10, 'https://picsum.photos/seed/gadoi1/800/500',    1, N'Gà đồi đặc sản Hòa Lạc'),
(11, 'https://picsum.photos/seed/tchlang1/800/500',  1, N'The Coffee House Láng Hạ'),
(12, 'https://picsum.photos/seed/thule1/800/500',    1, N'Công viên Thủ Lệ');
GO

-- ==============================================================================
-- SEED DATA: Thuộc tính địa điểm
-- ==============================================================================
-- Văn Miếu
INSERT INTO dbo.PlaceAttributes (PlaceId, GroupName, Label, IsAvailable) VALUES
(2, N'Khả năng tiếp cận', N'Lối vào cho xe lăn',   1),
(2, N'Tùy chọn dịch vụ', N'Có hướng dẫn viên',     1),
(2, N'Tùy chọn dịch vụ', N'Bản đồ tham quan',       1),
(2, N'Tiện ích',          N'Bãi đỗ xe',              1),
(2, N'Tiện ích',          N'Nhà vệ sinh công cộng',  1),
-- Bún Chả Hương Liên
(5, N'Tùy chọn dịch vụ', N'Ăn tại chỗ',             1),
(5, N'Tùy chọn dịch vụ', N'Mang về',                 1),
(5, N'Tùy chọn bữa ăn',  N'Bữa trưa',               1),
(5, N'Tùy chọn bữa ăn',  N'Bữa tối',                1),
(5, N'Điểm nổi bật',     N'Đặc sản Hà Nội',         1),
-- Làng Văn hóa
(7, N'Tùy chọn dịch vụ', N'Khu dã ngoại',           1),
(7, N'Tiện ích',          N'Khu nướng BBQ',           1),
(7, N'Tiện ích',          N'Thuê thuyền kayak',       1),
(7, N'Tiện ích',          N'Phù hợp trẻ nhỏ',        1),
(7, N'Tiện ích',          N'Bãi đỗ xe rộng',         1),
-- FPT Uni
(8, N'Tùy chọn dịch vụ', N'Cho phép tham quan',     1),
(8, N'Tiện ích',          N'WiFi công cộng',          1),
(8, N'Tiện ích',          N'Khu ăn uống sinh viên',  1),
-- Nhà hàng gà đồi
(10, N'Tùy chọn dịch vụ',N'Đặt bàn trước',          1),
(10, N'Tùy chọn bữa ăn', N'Bữa trưa',               1),
(10, N'Tùy chọn bữa ăn', N'Bữa tối',                1),
(10, N'Tiện ích',         N'Bãi đỗ xe rộng',         1),
(10, N'Tiện ích',         N'Phù hợp nhóm lớn',       1);
GO

-- ==============================================================================
-- SEED DATA: Tags cho địa điểm
-- ==============================================================================
INSERT INTO dbo.PlaceTags (PlaceId, TagId)
SELECT p.Id, t.Id FROM dbo.Places p, dbo.Tags t
WHERE (p.Id = 1  AND t.Name IN (N'Đi bộ', N'Chụp ảnh', N'Cặp đôi'))
   OR (p.Id = 2  AND t.Name IN (N'Lịch sử', N'Chụp ảnh', N'Gia đình'))
   OR (p.Id = 3  AND t.Name IN (N'Lịch sử', N'Đi bộ'))
   OR (p.Id = 4  AND t.Name IN (N'Cặp đôi', N'Đi bộ', N'Chụp ảnh'))
   OR (p.Id = 5  AND t.Name IN (N'Ăn uống', N'Nhóm bạn'))
   OR (p.Id = 6  AND t.Name IN (N'Cà phê', N'Cặp đôi', N'Chụp ảnh'))
   OR (p.Id = 7  AND t.Name IN (N'Dã ngoại', N'Gia đình', N'Nhóm bạn'))
   OR (p.Id = 8  AND t.Name IN (N'Sinh viên', N'Chụp ảnh'))
   OR (p.Id = 9  AND t.Name IN (N'Sinh viên'))
   OR (p.Id = 10 AND t.Name IN (N'Ăn uống', N'Nhóm bạn', N'Gia đình'))
   OR (p.Id = 11 AND t.Name IN (N'Cà phê', N'Sinh viên'))
   OR (p.Id = 12 AND t.Name IN (N'Gia đình', N'Vui chơi'));
GO

-- ==============================================================================
-- SEED DATA: Sự kiện
-- ==============================================================================
SET IDENTITY_INSERT dbo.Events ON;
INSERT INTO dbo.Events (Id, Title, Description, PlaceId, BannerImageUrl, StartAt, EndAt, Status, IsFeatured) VALUES
(1, N'Lễ hội Đua thuyền Đồng Mô 2026',
    N'Giải đua thuyền rồng truyền thống lớn nhất Bắc Bộ. Hàng chục đội đến từ các tỉnh thành tham gia tranh tài trên hồ Đồng Mô. Vào cửa tự do, có khu ẩm thực và trò chơi dân gian.',
    7, 'https://picsum.photos/seed/event-duathuyen/1200/500', '2026-05-15 08:00', '2026-05-17 18:00', 'upcoming', 1),
(2, N'Đêm nhạc Acoustic Hồ Gươm',
    N'Chuỗi sự kiện âm nhạc ngoài trời định kỳ tại bờ hồ Hoàn Kiếm. Quy tụ các nghệ sĩ indie và acoustic Hà Nội. Miễn phí, mang ghế hoặc trải chiếu tự do.',
    1, 'https://picsum.photos/seed/event-acoustic/1200/500', '2026-05-20 19:00', '2026-05-20 22:00', 'upcoming', 1),
(3, N'Hội chợ Ẩm thực Sinh viên FPT',
    N'Ngày hội ẩm thực do sinh viên ĐH FPT Hà Nội tổ chức. Hơn 50 gian hàng với món ăn từ các vùng miền. Có biểu diễn văn nghệ và hoạt động vui chơi.',
    8, 'https://picsum.photos/seed/event-amthuc/1200/500', '2026-06-05 09:00', '2026-06-05 21:00', 'upcoming', 0),
(4, N'Triển lãm "Hà Nội - 1000 Năm Văn Hiến"',
    N'Triển lãm ảnh và hiện vật về lịch sử Thăng Long - Hà Nội qua các thời kỳ. Tổ chức tại Văn Miếu, miễn phí cho học sinh sinh viên.',
    2, 'https://picsum.photos/seed/event-vanhien/1200/500', '2026-06-01 08:00', '2026-06-30 18:00', 'upcoming', 1);
SET IDENTITY_INSERT dbo.Events OFF;
GO

-- ==============================================================================
-- SEED DATA: Bài đăng Newsfeed
-- ==============================================================================
INSERT INTO dbo.FeedPosts (Title, Summary, CoverImageUrl, Type, PublishedAt, IsPinned, EventId) VALUES
(N'Top 5 địa điểm dã ngoại cuối tuần gần Hà Nội',
 N'Khám phá những điểm cắm trại, BBQ và chèo thuyền chỉ cách trung tâm 30-50km.',
 'https://picsum.photos/seed/feed-dango/800/400', 'guide', SYSUTCDATETIME(), 1, NULL),

(N'Kinh nghiệm khám phá Làng Văn hóa các Dân tộc từ A-Z',
 N'Thuê lều, đặt BBQ, chèo thuyền kayak và những lưu ý bạn không được bỏ qua.',
 'https://picsum.photos/seed/feed-langvh/800/400', 'guide', SYSUTCDATETIME(), 0, NULL),

(N'Ăn gì quanh khu Đại học FPT & Hòa Lạc?',
 N'Tọa độ gà đồi, cá suối, cơm rang và cà phê bình dân cho dân sinh viên.',
 'https://picsum.photos/seed/feed-hoalac/800/400', 'tip', SYSUTCDATETIME(), 0, NULL),

(N'Cà phê view đẹp khu Láng Hạ - Đống Đa',
 N'Những quán cà phê yên tĩnh, có WiFi tốt, phù hợp ngồi học và làm việc cả ngày.',
 'https://picsum.photos/seed/feed-cafelang/800/400', 'tip', SYSUTCDATETIME(), 0, NULL),

(N'Lễ hội Đua thuyền Đồng Mô 2026 - Lịch trình chi tiết',
 N'Mọi thứ bạn cần biết về lễ hội thể thao dưới nước lớn nhất Bắc Bộ năm nay.',
 'https://picsum.photos/seed/feed-duathuyen/800/400', 'news', SYSUTCDATETIME(), 1, 1),

(N'Cà phê trứng Hà Nội - Uống đúng cách mới ngon',
 N'Bí quyết thưởng thức cà phê trứng đúng kiểu Hà thành, và những địa chỉ không thể bỏ qua.',
 'https://picsum.photos/seed/feed-cafetung/800/400', 'tip', SYSUTCDATETIME(), 0, NULL);
GO

-- Đánh giá mẫu
INSERT INTO dbo.PlaceReviews (PlaceId, UserId, QualityRating, ServiceRating, FoodRating, Content) VALUES
(1,  'seed-user-1', 5, 5, NULL, N'Đây là trái tim của Hà Nội! Mỗi buổi sáng đi bộ quanh hồ là cách khởi đầu ngày mới tuyệt vời nhất.'),
(5,  'seed-user-2', 5, 4, 5,    N'Bún chả ngon nhất Hà Nội! Giá hợp lý, phục vụ nhanh, không cần hỏi tại sao Obama lại chọn quán này.'),
(7,  'seed-user-3', 4, 4, NULL, N'Không gian tuyệt vời cho gia đình. Trẻ con thích lắm, người lớn cũng vui. Lần sau sẽ đặt trước khu BBQ.'),
(8,  'seed-user-4', 5, 5, NULL, N'Khuôn viên đẹp không tưởng! Kiến trúc hiện đại giữa thiên nhiên xanh mát. Ai chưa đến Hòa Lạc nhất định phải ghé.'),
(10, 'seed-user-5', 4, 4, 5,    N'Gà đồi ngon, không gian thoáng mát. Phù hợp đi theo nhóm đông. Nhớ đặt bàn trước cuối tuần nhé!');
GO

PRINT N'===== HOÀN TẤT: CityScout database đã sẵn sàng với dữ liệu Hà Nội =====';
