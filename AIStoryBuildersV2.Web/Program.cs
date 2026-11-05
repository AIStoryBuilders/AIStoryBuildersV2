using AIStoryBuildersV2.Shared;
using AIStoryBuildersV2.Shared.Hosting;
using AIStoryBuildersV2.Shared.Stories;
using AIStoryBuildersV2.Shared.Stories.Storage;
using AIStoryBuildersV2.Web.Services;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The open story is extracted into the .NET in-memory file system (MEMFS); the durable copy is a zip in localStorage (D3, D5).
var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
var root = Path.Combine(string.IsNullOrEmpty(documents) ? "/home/web_user/Documents" : documents, "AIStoryBuilders");
Directory.CreateDirectory(root);

builder.Services.AddBlazoredLocalStorageAsSingleton();
// AI providers are called directly from the browser, so the client has no base address.
builder.Services.AddSingleton(_ => new HttpClient());
builder.Services.AddSingleton<IHostCapabilities>(new BrowserHostCapabilities(root));
builder.Services.AddScoped<IFileExportService, BrowserFileExportService>();
builder.Services.AddSingleton<IStoryPersistence, ZipStoryPersistence>();
builder.Services.AddAIStoryBuildersV2Shared();

await builder.Build().RunAsync();
