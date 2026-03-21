using DocumentAIService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registrar serviços
builder.Services.AddScoped<IOcrService, OcrService>();
builder.Services.AddScoped<IDocumentAnalysisService, DocumentAnalysisService>();
builder.Services.AddScoped<IPdfConverterService, PdfConverterService>();

// Adicionar CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("AllowAll");
app.MapControllers();

// Servir index.html para rotas não encontradas (SPA fallback)
app.MapFallbackToFile("index.html");

app.Run();
