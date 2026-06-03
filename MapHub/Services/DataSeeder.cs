using MapHub.Data;
using MapHub.Models;
using Microsoft.EntityFrameworkCore;

namespace MapHub.Services;

public static class DataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        if (await db.Places.AnyAsync()) return; // already seeded

        var places = new List<Place>
        {
            new() { Name="Hồ Hoàn Kiếm", Address="Hoàn Kiếm, Hà Nội", Category="nature",
                    Latitude=21.028511m, Longitude=105.852014m, Visibility="public", IsApproved=true, IsFeatured=true,
                    About="Không gian đi bộ cuối tuần, nhiều cây xanh, không khí mát mẻ.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Văn Miếu - Quốc Tử Giám", Address="58 Quốc Tử Giám, Đống Đa, Hà Nội", Category="culture",
                    Latitude=21.027763m, Longitude=105.835552m, Visibility="public", IsApproved=true, IsFeatured=true,
                    MinPrice=30000, MaxPrice=70000, About="Di tích lịch sử văn hóa cấp quốc gia đặc biệt.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Chùa Láng (Chiêu Thiền Tự)", Address="Phố Chùa Láng, Đống Đa, Hà Nội", Category="temple",
                    Latitude=21.021144m, Longitude=105.817195m, Visibility="public", IsApproved=true, IsFeatured=false,
                    About="Không gian yên bình để thư giãn tịnh tâm.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Hồ Tây", Address="Tây Hồ, Hà Nội", Category="nature",
                    Latitude=21.057m, Longitude=105.825m, Visibility="public", IsApproved=true, IsFeatured=true,
                    About="Phù hợp đạp xe, chạy bộ, ngắm hoàng hôn.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Nhà hàng Bún Chả Hương Liên", Address="24 Lê Văn Hưu, Hai Bà Trưng, Hà Nội", Category="restaurant",
                    Latitude=21.01258m, Longitude=105.85138m, Visibility="public", IsApproved=true, IsFeatured=true,
                    MinPrice=60000, MaxPrice=120000, About="Thực đơn đặc trưng với bún chả than hoa, nem cuốn giòn rụm.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Cà phê Giảng (Egg Coffee)", Address="39 Nguyễn Hữu Huân, Hoàn Kiếm, Hà Nội", Category="cafe",
                    Latitude=21.03421m, Longitude=105.8516m, Visibility="public", IsApproved=true, IsFeatured=true,
                    MinPrice=30000, MaxPrice=60000, About="Không gian vintage nhỏ, cà phê trứng truyền thống từ 1946.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Làng Văn hóa Du lịch các Dân tộc VN", Address="Đồng Mô, Sơn Tây, Hà Nội", Category="culture",
                    Latitude=21.035m, Longitude=105.394m, Visibility="public", IsApproved=true, IsFeatured=false,
                    MinPrice=30000, MaxPrice=100000, About="Khu dã ngoại lý tưởng, nướng BBQ, trải nghiệm văn hóa đặc sắc.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Twitter Beans Coffee", Address="Khu CNC Hòa Lạc, Thạch Thất, Hà Nội", Category="cafe",
                    Latitude=21.0007m, Longitude=105.5374m, Visibility="public", IsApproved=true, IsFeatured=false,
                    MinPrice=35000, MaxPrice=75000, About="Không gian yên tĩnh, wifi nhanh, ổ cắm điện tại bàn.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Bảo tàng Dân tộc học Việt Nam", Address="Nguyễn Văn Huyên, Cầu Giấy, Hà Nội", Category="culture",
                    Latitude=21.0375m, Longitude=105.8059m, Visibility="public", IsApproved=true, IsFeatured=true,
                    MinPrice=40000, MaxPrice=40000, About="Kiến trúc độc đáo, có nhà dài và nhà rông Tây Nguyên ngoài trời.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
            new() { Name="Công viên Thống Nhất", Address="Trần Nhân Tông, Hai Bà Trưng, Hà Nội", Category="nature",
                    Latitude=21.015m, Longitude=105.846m, Visibility="public", IsApproved=true, IsFeatured=false,
                    About="Không gian thoáng đãng, hồ đạp vịt, phù hợp đi bộ cuối tuần.", CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow },
        };
        db.Places.AddRange(places);
        await db.SaveChangesAsync();

        var tags = new List<Tag>
        {
            new() { Name="Khách sạn" }, new() { Name="Sự kiện" },
            new() { Name="Khu vui chơi" }, new() { Name="Bảo tàng" }, new() { Name="Ăn uống" }
        };
        db.Tags.AddRange(tags);

        db.Events.AddRange(
            new Event { Title="Triển lãm \"Hà Nội - 1000 Năm Văn Hiến\"",
                Description="Triển lãm ảnh và hiện vật về lịch sử Thăng Long. Miễn phí cho học sinh sinh viên.",
                StartAt=new DateTime(2026,6,1,0,0,0,DateTimeKind.Utc), EndAt=new DateTime(2026,6,30,0,0,0,DateTimeKind.Utc),
                IsFeatured=true, Status="ongoing", CreatedAt=DateTime.UtcNow },
            new Event { Title="Lễ hội Đua thuyền Đồng Mô",
                Description="Lễ hội thể thao dưới nước lớn nhất Bắc Bộ.",
                StartAt=new DateTime(2026,7,15,0,0,0,DateTimeKind.Utc), EndAt=new DateTime(2026,7,17,0,0,0,DateTimeKind.Utc),
                IsFeatured=true, Status="upcoming", CreatedAt=DateTime.UtcNow },
            new Event { Title="Đêm nhạc Acoustic Hồ Tây",
                Description="Giao lưu âm nhạc dưới ánh đèn phố bên bờ hồ Tây.",
                StartAt=new DateTime(2026,6,20,0,0,0,DateTimeKind.Utc), EndAt=new DateTime(2026,6,20,0,0,0,DateTimeKind.Utc),
                IsFeatured=false, Status="upcoming", CreatedAt=DateTime.UtcNow }
        );

        await db.SaveChangesAsync();
    }
}
