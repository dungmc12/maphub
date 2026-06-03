-- ==============================================================================
-- BƯỚC 1: XÓA DATABASE CŨ (chạy trong SSMS với master database)
-- Đổi sang master trước: USE master;
-- ==============================================================================

USE master;
GO

IF EXISTS (SELECT 1 FROM sys.databases WHERE name = 'MapHubDB')
BEGIN
    ALTER DATABASE MapHubDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE MapHubDB;
    PRINT N'✓ Đã xóa MapHubDB';
END
ELSE
    PRINT N'- MapHubDB chưa tồn tại, bỏ qua';
GO

PRINT N'=> Chạy app lên để EF tự tạo lại MapHubDB với đúng schema';
PRINT N'=> Sau khi app khởi động thành công, chạy tiếp phần BƯỚC 2 bên dưới';
GO

-- ==============================================================================
-- BƯỚC 2: SEED DATA (chạy SAU KHI app đã khởi động thành công)
-- Lúc này database đã có đúng schema từ EF migrations
-- ==============================================================================

USE MapHubDB;
GO

-- Seed Tags
IF NOT EXISTS (SELECT 1 FROM dbo.Tags)
BEGIN
    INSERT INTO dbo.Tags (Name) VALUES
    (N'Đi bộ'), (N'Gia đình'), (N'Cặp đôi'), (N'Nhóm bạn'), (N'Chụp ảnh'),
    (N'Dã ngoại'), (N'Ăn uống'), (N'Cà phê'), (N'Lịch sử'), (N'Thiên nhiên'),
    (N'Mua sắm'), (N'Nightlife'), (N'Thể thao'), (N'Âm nhạc'), (N'Sinh viên');
    PRINT N'✓ Seed Tags xong';
END
GO

-- Seed Places
IF NOT EXISTS (SELECT 1 FROM dbo.Places WHERE Id = 1)
BEGIN
    SET IDENTITY_INSERT dbo.Places ON;

    INSERT INTO dbo.Places
        (Id, Name, Description, About, Address,
         Latitude, Longitude, Category, Visibility, IsApproved,
         MinPrice, MaxPrice, Phone, WebsiteUrl,
         CreatedAt, UpdatedAt)
    VALUES
    (1,  N'Hồ Hoàn Kiếm',
         N'Trái tim xanh của Hà Nội với tháp Rùa và đền Ngọc Sơn huyền thoại.',
         N'Không gian đi bộ cuối tuần yêu thích của người Hà Nội. Bờ hồ thoáng mát với nhiều ghế đá, phù hợp đi dạo buổi sáng sớm hoặc tối mát. Xung quanh có nhiều hàng quán, trà đá vỉa hè đặc trưng.',
         N'Hoàn Kiếm, Hà Nội', 21.028511, 105.852014,
         N'nature', 'public', 1, 0, 0, NULL, NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (2,  N'Văn Miếu - Quốc Tử Giám',
         N'Trường đại học đầu tiên của Việt Nam, biểu tượng văn hiến ngàn năm.',
         N'Di tích lịch sử văn hóa cấp quốc gia đặc biệt. Khu vực có 5 sân, 82 bia tiến sĩ từ thế kỷ XV. Có hướng dẫn viên tiếng Việt và tiếng Anh.',
         N'58 Quốc Tử Giám, Đống Đa, Hà Nội', 21.027763, 105.835552,
         N'culture', 'public', 1, 30000, 70000, N'024 3845 2917', NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (3,  N'Chùa Láng (Chiêu Thiền Tự)',
         N'Ngôi chùa cổ kính hơn 800 năm tuổi, không gian yên bình giữa lòng thành phố.',
         N'Chùa nằm giữa khu dân cư phố Chùa Láng, có nhiều cây cổ thụ bóng mát. Không thu phí vào cổng.',
         N'Phố Chùa Láng, Đống Đa, Hà Nội', 21.021144, 105.817195,
         N'temple', 'public', 1, 0, 0, NULL, NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (4,  N'Hồ Tây',
         N'Hồ nước ngọt tự nhiên lớn nhất Hà Nội, nơi hẹn hò lý tưởng buổi chiều tà.',
         N'Chu vi hơn 17km, xung quanh có đường dạo bộ và xe đạp. Bờ hồ phía Nam (Trúc Bạch) có nhiều quán cafe view hồ đẹp.',
         N'Tây Hồ, Hà Nội', 21.059088, 105.823047,
         N'nature', 'public', 1, 0, 0, NULL, NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (5,  N'Nhà hàng Bún Chả Hương Liên',
         N'Quán bún chả nổi tiếng thế giới, nơi Tổng thống Obama từng ghé thăm năm 2016.',
         N'Bún chả và nem rán là món must-try. Thường đông vào giờ trưa nên đến trước 11h30 hoặc sau 13h30.',
         N'24 Lê Văn Hưu, Hai Bà Trưng, Hà Nội', 21.021667, 105.852500,
         N'restaurant', 'public', 1, 40000, 80000, N'024 3943 4106', NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (6,  N'Cà phê Giảng (Egg Coffee)',
         N'Nơi phát minh ra cà phê trứng - đặc sản nổi tiếng nhất của Hà Nội.',
         N'Quán nhỏ, leo cầu thang lên tầng 2 có view nhìn xuống phố cổ. Cà phê trứng phải thưởng thức nóng.',
         N'39 Nguyễn Hữu Huân, Hoàn Kiếm, Hà Nội', 21.035600, 105.851200,
         N'cafe', 'public', 1, 25000, 55000, NULL, NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (7,  N'Làng Văn hóa Du lịch các Dân tộc VN',
         N'Không gian dã ngoại rộng lớn với kiến trúc 54 dân tộc, phù hợp cả gia đình.',
         N'Khu du lịch rộng hàng trăm ha tại Đồng Mô. Có khu cắm trại, BBQ, thuyền kayak. Cách HN khoảng 40km.',
         N'Đồng Mô, Sơn Tây, Hà Nội', 21.038700, 105.394000,
         N'entertainment', 'public', 1, 30000, 150000, N'024 3368 3684', NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (8,  N'Đại học FPT Hà Nội',
         N'Khuôn viên đại học hiện đại bậc nhất Việt Nam, kiến trúc ấn tượng tại Hòa Lạc.',
         N'Thiết kế bởi KTS Võ Trọng Nghĩa. Khuôn viên 30ha với sân chơi, ký túc xá, trung tâm thể thao.',
         N'Km29 Đại lộ Thăng Long, Thạch Thất, Hà Nội', 21.012789, 105.525678,
         N'education', 'public', 1, 0, 0, N'024 7300 1866', N'https://hanoi.fpt.edu.vn',
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (9,  N'Khu Công nghệ cao Hòa Lạc',
         N'Trung tâm R&D và công nghệ lớn nhất Việt Nam, nơi quy tụ FPT, Viettel, Samsung...',
         N'Khu công nghệ cao rộng 1.586ha. Đi theo Đại lộ Thăng Long km29.',
         N'Km29 Đại lộ Thăng Long, Thạch Thất, Hà Nội', 21.007410, 105.525229,
         N'other', 'public', 1, 0, 0, NULL, NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (10, N'Nhà hàng Gà Đồi Hòa Lạc',
         N'Gà đồi thả vườn đặc sản vùng Ba Vì, không gian thoáng mát giữa thiên nhiên.',
         N'Chuyên gà đồi, dê núi và đặc sản vùng núi. Phong cách nhà hàng vườn, bãi đỗ xe rộng.',
         N'Quốc lộ 21, Thạch Thất, Hà Nội', 21.010500, 105.540000,
         N'restaurant', 'public', 1, 150000, 500000, N'0912 345 678', NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (11, N'The Coffee House - Láng Hạ',
         N'Không gian làm việc thoải mái, WiFi tốt, gần khu văn phòng Láng Hạ.',
         N'Chi nhánh rộng rãi, tầng 2 view ra phố. WiFi ổn định, phù hợp ngồi làm việc cả ngày.',
         N'187 Láng Hạ, Đống Đa, Hà Nội', 21.019800, 105.820400,
         N'cafe', 'public', 1, 39000, 85000, NULL, NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME()),

    (12, N'Công viên Thủ Lệ (Vườn thú HN)',
         N'Vườn thú kết hợp công viên cây xanh, điểm vui chơi quen thuộc cho gia đình Hà Nội.',
         N'Khu vui chơi 35ha với nhiều loài động vật, hồ nước, cầu trượt, khu picnic.',
         N'Kim Mã, Ba Đình, Hà Nội', 21.033900, 105.831500,
         N'entertainment', 'public', 1, 30000, 60000, NULL, NULL,
         SYSUTCDATETIME(), SYSUTCDATETIME());

    SET IDENTITY_INSERT dbo.Places OFF;
    PRINT N'✓ Seed Places (12 địa điểm) xong';
END
GO

-- Seed PlaceImages
IF NOT EXISTS (SELECT 1 FROM dbo.PlaceImages)
BEGIN
    INSERT INTO dbo.PlaceImages (PlaceId, Url, IsPrimary, Caption, CreatedAt) VALUES
    (1,  'https://picsum.photos/seed/hoguom1/800/500',    1, N'Hồ Hoàn Kiếm nhìn từ bờ Đinh Tiên Hoàng', SYSUTCDATETIME()),
    (1,  'https://picsum.photos/seed/hoguom2/800/500',    0, N'Tháp Rùa lúc hoàng hôn',                  SYSUTCDATETIME()),
    (2,  'https://picsum.photos/seed/vanmieu1/800/500',   1, N'Cổng Văn Miếu Môn',                        SYSUTCDATETIME()),
    (2,  'https://picsum.photos/seed/vanmieu2/800/500',   0, N'Khuê Văn Các',                             SYSUTCDATETIME()),
    (3,  'https://picsum.photos/seed/chualang1/800/500',  1, N'Chùa Láng cổng chính',                     SYSUTCDATETIME()),
    (4,  'https://picsum.photos/seed/hotay1/800/500',     1, N'Hồ Tây lúc chiều tà',                      SYSUTCDATETIME()),
    (5,  'https://picsum.photos/seed/buncha1/800/500',    1, N'Bún chả Hương Liên',                        SYSUTCDATETIME()),
    (5,  'https://picsum.photos/seed/buncha2/800/500',    0, N'Không gian quán',                           SYSUTCDATETIME()),
    (6,  'https://picsum.photos/seed/cafegiang1/800/500', 1, N'Cà phê trứng đặc trưng',                   SYSUTCDATETIME()),
    (7,  'https://picsum.photos/seed/langvh1/800/500',    1, N'Toàn cảnh Làng Văn hóa',                   SYSUTCDATETIME()),
    (7,  'https://picsum.photos/seed/langvh2/800/500',    0, N'Khu hồ Đồng Mô',                           SYSUTCDATETIME()),
    (8,  'https://picsum.photos/seed/fpthn1/800/500',     1, N'Khuôn viên ĐH FPT Hà Nội',                 SYSUTCDATETIME()),
    (8,  'https://picsum.photos/seed/fpthn2/800/500',     0, N'Tòa nhà hiện đại ĐH FPT',                  SYSUTCDATETIME()),
    (9,  'https://picsum.photos/seed/hoalac1/800/500',    1, N'Khu CNC Hòa Lạc',                          SYSUTCDATETIME()),
    (10, 'https://picsum.photos/seed/gadoi1/800/500',     1, N'Gà đồi đặc sản Hòa Lạc',                  SYSUTCDATETIME()),
    (11, 'https://picsum.photos/seed/tchlang1/800/500',   1, N'The Coffee House Láng Hạ',                 SYSUTCDATETIME()),
    (12, 'https://picsum.photos/seed/thule1/800/500',     1, N'Công viên Thủ Lệ',                         SYSUTCDATETIME());
    PRINT N'✓ Seed PlaceImages xong';
END
GO

-- Seed PlaceAttributes
IF NOT EXISTS (SELECT 1 FROM dbo.PlaceAttributes)
BEGIN
    INSERT INTO dbo.PlaceAttributes (PlaceId, GroupName, Label, IsAvailable) VALUES
    (2,  N'Khả năng tiếp cận', N'Lối vào cho xe lăn',    1),
    (2,  N'Tùy chọn dịch vụ', N'Có hướng dẫn viên',      1),
    (2,  N'Tùy chọn dịch vụ', N'Bản đồ tham quan',        1),
    (2,  N'Tiện ích',          N'Bãi đỗ xe',               1),
    (2,  N'Tiện ích',          N'Nhà vệ sinh công cộng',   1),
    (5,  N'Tùy chọn dịch vụ', N'Ăn tại chỗ',              1),
    (5,  N'Tùy chọn dịch vụ', N'Mang về',                  1),
    (5,  N'Tùy chọn bữa ăn',  N'Bữa trưa',                1),
    (5,  N'Tùy chọn bữa ăn',  N'Bữa tối',                 1),
    (5,  N'Điểm nổi bật',     N'Đặc sản Hà Nội',          1),
    (7,  N'Tùy chọn dịch vụ', N'Khu dã ngoại',            1),
    (7,  N'Tiện ích',          N'Khu nướng BBQ',            1),
    (7,  N'Tiện ích',          N'Thuê thuyền kayak',        1),
    (7,  N'Tiện ích',          N'Phù hợp trẻ nhỏ',         1),
    (7,  N'Tiện ích',          N'Bãi đỗ xe rộng',          1),
    (8,  N'Tùy chọn dịch vụ', N'Cho phép tham quan',      1),
    (8,  N'Tiện ích',          N'WiFi công cộng',           1),
    (8,  N'Tiện ích',          N'Khu ăn uống sinh viên',   1),
    (10, N'Tùy chọn dịch vụ', N'Đặt bàn trước',           1),
    (10, N'Tùy chọn bữa ăn',  N'Bữa trưa',                1),
    (10, N'Tùy chọn bữa ăn',  N'Bữa tối',                 1),
    (10, N'Tiện ích',          N'Bãi đỗ xe rộng',          1),
    (10, N'Tiện ích',          N'Phù hợp nhóm lớn',        1);
    PRINT N'✓ Seed PlaceAttributes xong';
END
GO

-- Seed PlaceTags
IF NOT EXISTS (SELECT 1 FROM dbo.PlaceTags)
BEGIN
    INSERT INTO dbo.PlaceTags (PlaceId, TagId)
    SELECT p.Id, t.Id FROM dbo.Places p
    CROSS JOIN dbo.Tags t
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
       OR (p.Id = 12 AND t.Name IN (N'Gia đình'));
    PRINT N'✓ Seed PlaceTags xong';
END
GO

-- Seed Events
IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE Id = 1)
BEGIN
    SET IDENTITY_INSERT dbo.Events ON;
    INSERT INTO dbo.Events (Id, Title, Description, PlaceId, BannerImageUrl, StartAt, EndAt, Status, IsFeatured, CreatedAt) VALUES
    (1, N'Lễ hội Đua thuyền Đồng Mô 2026',
        N'Giải đua thuyền rồng truyền thống lớn nhất Bắc Bộ. Hàng chục đội tham gia tranh tài trên hồ Đồng Mô. Vào cửa tự do, có khu ẩm thực và trò chơi dân gian.',
        7, 'https://picsum.photos/seed/event-duathuyen/1200/500',
        '2026-05-15 08:00', '2026-05-17 18:00', 'upcoming', 1, SYSUTCDATETIME()),
    (2, N'Đêm nhạc Acoustic Hồ Gươm',
        N'Chuỗi sự kiện âm nhạc ngoài trời tại bờ hồ Hoàn Kiếm. Quy tụ các nghệ sĩ indie Hà Nội. Miễn phí.',
        1, 'https://picsum.photos/seed/event-acoustic/1200/500',
        '2026-05-20 19:00', '2026-05-20 22:00', 'upcoming', 1, SYSUTCDATETIME()),
    (3, N'Hội chợ Ẩm thực Sinh viên FPT',
        N'Ngày hội ẩm thực do sinh viên ĐH FPT Hà Nội tổ chức. Hơn 50 gian hàng, biểu diễn văn nghệ.',
        8, 'https://picsum.photos/seed/event-amthuc/1200/500',
        '2026-06-05 09:00', '2026-06-05 21:00', 'upcoming', 0, SYSUTCDATETIME()),
    (4, N'Triển lãm "Hà Nội - 1000 Năm Văn Hiến"',
        N'Triển lãm ảnh và hiện vật về lịch sử Thăng Long. Miễn phí cho học sinh sinh viên.',
        2, 'https://picsum.photos/seed/event-vanhien/1200/500',
        '2026-06-01 08:00', '2026-06-30 18:00', 'upcoming', 1, SYSUTCDATETIME());
    SET IDENTITY_INSERT dbo.Events OFF;
    PRINT N'✓ Seed Events (4 sự kiện) xong';
END
GO

-- Seed FeedPosts
IF NOT EXISTS (SELECT 1 FROM dbo.FeedPosts)
BEGIN
    INSERT INTO dbo.FeedPosts (Title, Summary, CoverImageUrl, Type, PublishedAt, IsPinned, EventId) VALUES
    (N'Top 5 địa điểm dã ngoại cuối tuần gần Hà Nội',
     N'Khám phá những điểm cắm trại, BBQ và chèo thuyền chỉ cách trung tâm 30-50km.',
     'https://picsum.photos/seed/feed-dango/800/400', 'guide', SYSUTCDATETIME(), 1, NULL),
    (N'Kinh nghiệm khám phá Làng Văn hóa các Dân tộc từ A-Z',
     N'Thuê lều, đặt BBQ, chèo thuyền kayak và những lưu ý không được bỏ qua.',
     'https://picsum.photos/seed/feed-langvh/800/400', 'guide', SYSUTCDATETIME(), 0, NULL),
    (N'Ăn gì quanh khu Đại học FPT & Hòa Lạc?',
     N'Tọa độ gà đồi, cá suối, cơm rang và cà phê bình dân cho dân sinh viên.',
     'https://picsum.photos/seed/feed-hoalac/800/400', 'tip', SYSUTCDATETIME(), 0, NULL),
    (N'Cà phê view đẹp khu Láng Hạ - Đống Đa',
     N'Những quán cà phê yên tĩnh, WiFi tốt, phù hợp ngồi học và làm việc cả ngày.',
     'https://picsum.photos/seed/feed-cafelang/800/400', 'tip', SYSUTCDATETIME(), 0, NULL),
    (N'Lễ hội Đua thuyền Đồng Mô 2026 - Lịch trình chi tiết',
     N'Mọi thứ bạn cần biết về lễ hội thể thao dưới nước lớn nhất Bắc Bộ năm nay.',
     'https://picsum.photos/seed/feed-duathuyen/800/400', 'news', SYSUTCDATETIME(), 1, 1),
    (N'Cà phê trứng Hà Nội - Uống đúng cách mới ngon',
     N'Bí quyết thưởng thức cà phê trứng đúng kiểu Hà thành và địa chỉ không thể bỏ qua.',
     'https://picsum.photos/seed/feed-cafetung/800/400', 'tip', SYSUTCDATETIME(), 0, NULL);
    PRINT N'✓ Seed FeedPosts (6 bài viết) xong';
END
GO

-- Seed PlaceReviews
IF NOT EXISTS (SELECT 1 FROM dbo.PlaceReviews)
BEGIN
    INSERT INTO dbo.PlaceReviews (PlaceId, UserId, QualityRating, ServiceRating, FoodRating, Content, CreatedAt) VALUES
    (1,  'seed-user-1', 5, 5, NULL, N'Đây là trái tim của Hà Nội! Mỗi buổi sáng đi bộ quanh hồ là cách khởi đầu ngày mới tuyệt vời nhất.', SYSUTCDATETIME()),
    (5,  'seed-user-2', 5, 4, 5,    N'Bún chả ngon nhất Hà Nội! Giá hợp lý, phục vụ nhanh. Không cần hỏi tại sao Obama chọn quán này.', SYSUTCDATETIME()),
    (7,  'seed-user-3', 4, 4, NULL, N'Không gian tuyệt vời cho gia đình. Trẻ con thích lắm. Lần sau sẽ đặt trước khu BBQ.', SYSUTCDATETIME()),
    (8,  'seed-user-4', 5, 5, NULL, N'Khuôn viên đẹp không tưởng! Kiến trúc hiện đại giữa thiên nhiên xanh mát. Nhất định phải ghé.', SYSUTCDATETIME()),
    (10, 'seed-user-5', 4, 4, 5,    N'Gà đồi ngon, không gian thoáng. Nhớ đặt bàn trước cuối tuần nhé!', SYSUTCDATETIME());
    PRINT N'✓ Seed PlaceReviews xong';
END
GO

PRINT N'';
PRINT N'===== HOÀN TẤT: MapHubDB đã có đầy đủ dữ liệu =====';
GO
