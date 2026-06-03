using MapHub.Data;
using MapHub.Models;
using MapHub.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var rawConn = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured.");

// Render provides PostgreSQL URI (postgresql://user:pass@host:5432/db).
// Npgsql EF Core requires keyword=value format — convert if needed.
var connectionString = rawConn;
if (rawConn.StartsWith("postgresql://") || rawConn.StartsWith("postgres://"))
{
    var uri = new Uri(rawConn);
    var parts = uri.UserInfo.Split(':', 2);
    var port = uri.Port > 0 ? uri.Port : 5432;
    connectionString = $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};" +
                       $"Username={Uri.UnescapeDataString(parts[0])};Password={Uri.UnescapeDataString(parts[1])};" +
                       "SSL Mode=Require;Trust Server Certificate=true;";
}

var disableHttpsRedirection = builder.Configuration.GetValue<bool>("DisableHttpsRedirection");

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
    if (string.IsNullOrWhiteSpace(options.Model))
        options.Model = "gemini-2.0-flash";
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
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

builder.Services.AddHttpClient<IAiSuggestionService, AiSuggestionService>();
builder.Services.AddHttpClient<IAiChatService, AiChatService>();
builder.Services.AddHttpClient<IWeatherService, WeatherService>();

// Thanh toán Pro: VietQR + SePay
builder.Services.Configure<SePayOptions>(builder.Configuration.GetSection("SePay"));
builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.AddScoped<IProService, ProService>();
builder.Services.AddHostedService<ProExpiryService>();   // tự hạ Pro hết hạn về Free

var app = builder.Build();

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
