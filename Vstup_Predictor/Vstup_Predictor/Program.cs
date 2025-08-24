using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Vstup_Predictor.Components;
using Vstup_Predictor.Extensions;
using Vstup_Predictor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

Env.Load();

var connectionString = Env.GetString("MYSQL_CONNECTION_STRING") ?? throw new InvalidOperationException("Database conncetion string is not set in environment variables.");

// Add Database Context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        MySqlServerVersion.AutoDetect(connectionString)
    ));

// Http client and parser
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var proxyPath = Path.Combine(env.ContentRootPath, "proxies.txt");
    return new ProxyHttpClientFactory(proxyPath);
});
builder.Services.AddTransient<CustomHttpLoggingHandler>();
builder.Services.AddScoped<VstupParserService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Vstup_Predictor.Client._Imports).Assembly);

app.Run();
