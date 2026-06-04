using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
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
        options.Model = "gemini-flash-latest";
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
        options.User.RequireUniqueEmail = true;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

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
builder.Services.AddHostedService<ProExpiryService>();        // tự hạ Pro hết hạn về Free
builder.Services.AddHostedService<PaymentAutoCheckService>(); // tự kiểm tra CK qua Casso API → lên Pro

var app = builder.Build();

// Phải đặt TRƯỚC mọi middleware dùng tới scheme (https redirect, auth, OAuth callback)
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (!disableHttpsRedirection)
    app.UseHttpsRedirection();

app.UseStaticFiles();
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
