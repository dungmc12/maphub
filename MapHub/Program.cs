using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var rawConn = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured.");

// Detect provider: use postgres when DB_PROVIDER=postgres (Render) or when URI starts with postgresql://
var dbProvider = Environment.GetEnvironmentVariable("DB_PROVIDER") ?? "sqlserver";
var connectionString = rawConn;
if (rawConn.StartsWith("postgresql://") || rawConn.StartsWith("postgres://"))
{
    dbProvider = "postgres";
    var uri = new Uri(rawConn);
    var parts = uri.UserInfo.Split(':', 2);
    var port = uri.Port > 0 ? uri.Port : 5432;
    connectionString = $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};" +
                       $"Username={Uri.UnescapeDataString(parts[0])};Password={Uri.UnescapeDataString(parts[1])};" +
                       "SSL Mode=Require;Trust Server Certificate=true;";
}

var disableHttpsRedirection = builder.Configuration.GetValue<bool>("DisableHttpsRedirection");

// Render/Heroku... đứng trước app: tin X-Forwarded-Proto để Request.Scheme = https
// → Google OAuth redirect_uri dùng đúng https, không bị lệch khi đăng nhập trên server
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Nén Gzip/Brotli cho JSON/HTML/CSS/JS → giảm dung lượng truyền, tải nhanh hơn khi đông người
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
        .Concat(new[] { "application/json", "image/svg+xml" });
});
builder.Services.Configure<GoogleMapsOptions>(builder.Configuration.GetSection("GoogleMaps"));
builder.Services.Configure<AiAssistantOptions>(builder.Configuration.GetSection("AI"));

builder.Services.PostConfigure<GoogleMapsOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
        options.ApiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY") ?? string.Empty;
});
builder.Services.PostConfigure<AiAssistantOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
        options.ApiKey = Environment.GetEnvironmentVariable("AI__ApiKey") ?? string.Empty;

    // Ép về alias còn quota nếu Model trống hoặc là model free-tier đã cạn
    // (tránh phải sửa env AI__Model trên Render nếu nó còn để giá trị cũ)
    var deadModels = new[] { "gemini-2.0-flash", "gemini-2.5-flash", "gemini-2.0-flash-lite", "gemini-1.5-flash", "gemini-pro" };
    if (string.IsNullOrWhiteSpace(options.Model) ||
        deadModels.Contains(options.Model.Trim(), StringComparer.OrdinalIgnoreCase))
        options.Model = "gemini-3.1-flash-lite";
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (dbProvider == "postgres")
        options.UseNpgsql(connectionString);
    else
        options.UseSqlServer(connectionString);
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;  // tạm không bắt xác thực email — đăng ký bằng email là vào luôn
        options.User.RequireUniqueEmail = true;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;          // khớp với RegisterInputModel + giảm khả năng trùng mật khẩu đã rò rỉ
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
    // "Ghi nhớ đăng nhập": cookie persistent sống 30 ngày, trượt hạn mỗi lần dùng
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
});

// Lưu DataProtection keys vào DB (bền qua mỗi lần Render restart/redeploy) + tên app cố định.
// Nếu không, key tạo mới mỗi lần khởi động → mọi cookie đăng nhập bị vô hiệu = "Ghi nhớ" vô tác dụng.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("CityScout");

// Role/Pro thay đổi có hiệu lực trong ~1 phút (tự làm mới claims trong cookie) thay vì 30 phút mặc định
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.FromMinutes(1));

// Google OAuth
var googleClientId     = builder.Configuration["Google:ClientId"]
                         ?? Environment.GetEnvironmentVariable("Google__ClientId") ?? "";
var googleClientSecret = builder.Configuration["Google:ClientSecret"]
                         ?? Environment.GetEnvironmentVariable("Google__ClientSecret") ?? "";
if (!string.IsNullOrWhiteSpace(googleClientId))
{
    builder.Services.AddAuthentication()
        .AddGoogle(o =>
        {
            o.ClientId     = googleClientId;
            o.ClientSecret = googleClientSecret;
            o.CallbackPath = "/signin-google";
        });
}

builder.Services.AddHttpClient<IAiSuggestionService, AiSuggestionService>();
builder.Services.AddHttpClient<IAiChatService, AiChatService>();
builder.Services.AddHttpClient<IWeatherService, WeatherService>();

// Thanh toán Pro: PayOS + VietQR fallback
builder.Services.Configure<SePayOptions>(builder.Configuration.GetSection("SePay"));
builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.Configure<PayOSOptions>(builder.Configuration.GetSection("PayOS"));
builder.Services.Configure<CassoOptions>(builder.Configuration.GetSection("Casso"));
builder.Services.AddHttpClient<ICassoApiService, CassoApiService>();
builder.Services.AddHttpClient<PayOSService>();
builder.Services.AddScoped<IProService, ProService>();
builder.Services.AddScoped<IAppEmailSender, SmtpEmailSender>();  // gửi email xác thực tài khoản (Smtp__User/Smtp__Pass)
builder.Services.AddHostedService<ProExpiryService>();        // tự hạ Pro hết hạn về Free
builder.Services.AddHostedService<PaymentAutoCheckService>(); // tự kiểm tra CK qua Casso API → lên Pro

var app = builder.Build();

// Phải đặt TRƯỚC mọi middleware dùng tới scheme (https redirect, auth, OAuth callback)
app.UseForwardedHeaders();
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (!disableHttpsRedirection)
    app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    // CSS/JS dùng asp-append-version (hash đổi khi sửa) nên cache dài 7 ngày an toàn → tải lại nhanh
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=604800"
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Ensure upload folders exist
Directory.CreateDirectory(Path.Combine(app.Environment.WebRootPath, "uploads", "places"));
Directory.CreateDirectory(Path.Combine(app.Environment.WebRootPath, "uploads", "reviews"));
Directory.CreateDirectory(Path.Combine(app.Environment.WebRootPath, "uploads", "events"));

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    context.Database.EnsureCreated();

    // Auto-migration an toàn: thêm cột Latitude/Longitude cho Events nếu chưa có
    // (EnsureCreated không tự thêm cột vào bảng đã tồn tại). Idempotent, không phá dữ liệu.
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Events\" ADD COLUMN IF NOT EXISTS \"Latitude\" numeric NULL; " +
                "ALTER TABLE \"Events\" ADD COLUMN IF NOT EXISTS \"Longitude\" numeric NULL;");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('Events','Latitude') IS NULL ALTER TABLE Events ADD Latitude decimal(18,6) NULL; " +
                "IF COL_LENGTH('Events','Longitude') IS NULL ALTER TABLE Events ADD Longitude decimal(18,6) NULL;");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Auto-migration Events lat/lng lỗi");
    }

    // Tạo bảng lưu địa điểm yêu thích nếu chưa có (UserLists / UserListItems)
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"UserLists\" (" +
                "\"Id\" serial PRIMARY KEY, \"UserId\" text NOT NULL, \"Name\" text NOT NULL, " +
                "\"Description\" text NULL, \"CreatedAt\" timestamptz NOT NULL DEFAULT now()); " +
                "CREATE TABLE IF NOT EXISTS \"UserListItems\" (" +
                "\"ListId\" integer NOT NULL, \"PlaceId\" integer NOT NULL, " +
                "\"AddedAt\" timestamptz NOT NULL DEFAULT now(), " +
                "PRIMARY KEY (\"ListId\", \"PlaceId\"));");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF OBJECT_ID('UserLists') IS NULL CREATE TABLE UserLists (" +
                "Id int IDENTITY(1,1) PRIMARY KEY, UserId nvarchar(450) NOT NULL, Name nvarchar(max) NOT NULL, " +
                "Description nvarchar(max) NULL, CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()); " +
                "IF OBJECT_ID('UserListItems') IS NULL CREATE TABLE UserListItems (" +
                "ListId int NOT NULL, PlaceId int NOT NULL, AddedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), " +
                "CONSTRAINT PK_UserListItems PRIMARY KEY (ListId, PlaceId));");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Auto-migration UserLists lỗi");
    }

    // Cột mở rộng cho UserLists (Visibility, IconKey, UpdatedAt) — cho hệ thống Danh sách
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"UserLists\" ADD COLUMN IF NOT EXISTS \"Visibility\" text NOT NULL DEFAULT 'private'; " +
                "ALTER TABLE \"UserLists\" ADD COLUMN IF NOT EXISTS \"IconKey\" text NOT NULL DEFAULT 'favorites'; " +
                "ALTER TABLE \"UserLists\" ADD COLUMN IF NOT EXISTS \"UpdatedAt\" timestamptz NOT NULL DEFAULT now();");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('UserLists','Visibility') IS NULL ALTER TABLE UserLists ADD Visibility nvarchar(20) NOT NULL DEFAULT 'private'; " +
                "IF COL_LENGTH('UserLists','IconKey') IS NULL ALTER TABLE UserLists ADD IconKey nvarchar(20) NOT NULL DEFAULT 'favorites'; " +
                "IF COL_LENGTH('UserLists','UpdatedAt') IS NULL ALTER TABLE UserLists ADD UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME();");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Auto-migration UserLists cột mở rộng lỗi");
    }

    // Thêm cột mở rộng cho FeedPosts (Slug, Content, SEO, ViewCount)
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FeedPosts\" ADD COLUMN IF NOT EXISTS \"Slug\" text NULL; " +
                "ALTER TABLE \"FeedPosts\" ADD COLUMN IF NOT EXISTS \"Content\" text NULL; " +
                "ALTER TABLE \"FeedPosts\" ADD COLUMN IF NOT EXISTS \"SeoTitle\" text NULL; " +
                "ALTER TABLE \"FeedPosts\" ADD COLUMN IF NOT EXISTS \"SeoDescription\" text NULL; " +
                "ALTER TABLE \"FeedPosts\" ADD COLUMN IF NOT EXISTS \"ViewCount\" integer NOT NULL DEFAULT 0;");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('FeedPosts','Slug') IS NULL ALTER TABLE FeedPosts ADD Slug nvarchar(max) NULL; " +
                "IF COL_LENGTH('FeedPosts','Content') IS NULL ALTER TABLE FeedPosts ADD Content nvarchar(max) NULL; " +
                "IF COL_LENGTH('FeedPosts','SeoTitle') IS NULL ALTER TABLE FeedPosts ADD SeoTitle nvarchar(max) NULL; " +
                "IF COL_LENGTH('FeedPosts','SeoDescription') IS NULL ALTER TABLE FeedPosts ADD SeoDescription nvarchar(max) NULL; " +
                "IF COL_LENGTH('FeedPosts','ViewCount') IS NULL ALTER TABLE FeedPosts ADD ViewCount int NOT NULL DEFAULT 0;");
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Auto-migration FeedPosts mở rộng lỗi"); }

    // Thêm cột VideoUrl cho PlaceReviews nếu chưa có
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"PlaceReviews\" ADD COLUMN IF NOT EXISTS \"VideoUrl\" text NULL;");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('PlaceReviews','VideoUrl') IS NULL ALTER TABLE PlaceReviews ADD VideoUrl nvarchar(max) NULL;");
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Auto-migration PlaceReviews.VideoUrl lỗi"); }

    // Thêm cột TrialClaimed cho UserProfiles (đánh dấu đã dùng thử Pro) nếu chưa có
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"UserProfiles\" ADD COLUMN IF NOT EXISTS \"TrialClaimed\" boolean NOT NULL DEFAULT false;");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('UserProfiles','TrialClaimed') IS NULL ALTER TABLE UserProfiles ADD TrialClaimed bit NOT NULL DEFAULT 0;");
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Auto-migration UserProfiles.TrialClaimed lỗi"); }

    // Thêm cột Kind cho Payments (đánh dấu đăng ký mới / gia hạn) nếu chưa có
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Payments\" ADD COLUMN IF NOT EXISTS \"Kind\" text NULL;");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('Payments','Kind') IS NULL ALTER TABLE Payments ADD Kind nvarchar(max) NULL;");
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Auto-migration Payments.Kind lỗi"); }

    // Tạo bảng PasswordResetRequests (yêu cầu đổi mật khẩu) nếu chưa có
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"PasswordResetRequests\" (" +
                "\"Id\" serial PRIMARY KEY, \"Email\" text NOT NULL, \"Message\" text NULL, " +
                "\"Status\" text NOT NULL DEFAULT 'pending', \"NewPassword\" text NULL, " +
                "\"CreatedAt\" timestamptz NOT NULL DEFAULT now(), \"HandledAt\" timestamptz NULL);");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF OBJECT_ID('PasswordResetRequests','U') IS NULL CREATE TABLE PasswordResetRequests (" +
                "Id int IDENTITY(1,1) PRIMARY KEY, Email nvarchar(256) NOT NULL, Message nvarchar(max) NULL, " +
                "Status nvarchar(50) NOT NULL DEFAULT 'pending', NewPassword nvarchar(max) NULL, " +
                "CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), HandledAt datetime2 NULL);");
    }
    catch (Exception ex) { app.Logger.LogError(ex, "Auto-migration PasswordResetRequests lỗi"); }

    // Thêm cột Phone2 / OpenTime / CloseTime cho Places nếu chưa có
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Places\" ADD COLUMN IF NOT EXISTS \"Phone2\" text NULL; " +
                "ALTER TABLE \"Places\" ADD COLUMN IF NOT EXISTS \"OpenTime\" text NULL; " +
                "ALTER TABLE \"Places\" ADD COLUMN IF NOT EXISTS \"CloseTime\" text NULL;");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('Places','Phone2') IS NULL ALTER TABLE Places ADD Phone2 nvarchar(max) NULL; " +
                "IF COL_LENGTH('Places','OpenTime') IS NULL ALTER TABLE Places ADD OpenTime nvarchar(max) NULL; " +
                "IF COL_LENGTH('Places','CloseTime') IS NULL ALTER TABLE Places ADD CloseTime nvarchar(max) NULL;");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Auto-migration Places Phone2/OpenTime/CloseTime lỗi");
    }

    // Thêm cột IsMenu cho PlaceImages nếu chưa có (ảnh thực đơn/menu)
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"PlaceImages\" ADD COLUMN IF NOT EXISTS \"IsMenu\" boolean NOT NULL DEFAULT false; " +
                "ALTER TABLE \"PlaceImages\" ADD COLUMN IF NOT EXISTS \"IsVideo\" boolean NOT NULL DEFAULT false;");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF COL_LENGTH('PlaceImages','IsMenu') IS NULL ALTER TABLE PlaceImages ADD IsMenu bit NOT NULL DEFAULT 0; " +
                "IF COL_LENGTH('PlaceImages','IsVideo') IS NULL ALTER TABLE PlaceImages ADD IsVideo bit NOT NULL DEFAULT 0;");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Auto-migration PlaceImages.IsMenu lỗi");
    }

    // Bảng lưu DataProtection keys (EnsureCreated không tự thêm vào DB đã tồn tại)
    try
    {
        if (dbProvider == "postgres")
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"DataProtectionKeys\" (" +
                "\"Id\" serial PRIMARY KEY, \"FriendlyName\" text NULL, \"Xml\" text NULL);");
        else
            await context.Database.ExecuteSqlRawAsync(
                "IF OBJECT_ID('DataProtectionKeys') IS NULL CREATE TABLE DataProtectionKeys (" +
                "Id int IDENTITY(1,1) PRIMARY KEY, FriendlyName nvarchar(max) NULL, Xml nvarchar(max) NULL);");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Auto-migration DataProtectionKeys lỗi");
    }

    await DataSeeder.SeedAsync(context);

    // Tạo roles
    string[] roles = ["Admin", "Pro", "User"];
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Tạo tài khoản admin mặc định
    const string adminEmail = "admin@maphub.com";
    const string adminPass = "Admin@123";
    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(adminUser, adminPass);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
            // Tạo UserProfile cho admin
            context.UserProfiles.Add(new UserProfile
            {
                UserId = adminUser.Id,
                DisplayName = "Admin CityScout",
                Tier = "admin",
                MaxPlaces = int.MaxValue,
                MaxPlans = int.MaxValue
            });
            await context.SaveChangesAsync();
        }
    }
    else if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
    {
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }
}

app.Run();
