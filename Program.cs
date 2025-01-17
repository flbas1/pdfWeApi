using System.Globalization;
using CsvHelper;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();  // to redirect to https

app.UseDefaultFiles();  // Add this line to serve default files like index.html
app.UseStaticFiles();// to serve static files





app.MapGet("/data", (string csvFilePath) =>
{
    var records = new List<CsvDataModel>();
    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
    {
        return csv.GetRecords<CsvDataModel>().ToList();
    }
})
.WithName("GetData");

app.MapPost("/data", async (HttpContext context) =>
{
    var csvFilePath = context.Request.Query["csvFilePath"].ToString();
    var data = await context.Request.ReadFromJsonAsync<CsvDataModel>();

    if (data == null) return Results.BadRequest();

    var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, Delimiter = "," };

    var records = new List<CsvDataModel>();
    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, config))
    {
        records = csv.GetRecords<CsvDataModel>().ToList();
    }

    var existingRecord = records.FirstOrDefault(r => r.Field == data.Field);
    if (existingRecord != null)
    {
        existingRecord.Value = data.Value;
        existingRecord.Notes = data.Notes;
    }

    using (var writer = new StreamWriter(csvFilePath))
    using (var csv = new CsvWriter(writer, config))
    {
        csv.WriteRecords(records);
    }
    return Results.Ok(true);
})
.WithName("PostData");


//todo:  this should be post.  post is idempotent
app.MapPost("/pdf", () =>
{
    string csvFilePath = Path.Combine(Directory.GetCurrentDirectory(), "fullValues.csv");
    string inputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "902c10-21.pdf");
    string outputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "902c10-21_filled.pdf");

    // Open the existing PDF
    PdfDocument pdfDoc = new PdfDocument(new PdfReader(inputFilePath), new PdfWriter(outputFilePath));

    // Get the AcroForm from the PDF document
    PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, true);

    // Get all form fields
    IDictionary<string, PdfFormField> fields = form.GetFormFields();

    // Read field values from CSV file
    var records = new List<CsvDataModel>();
    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
    {
        records = csv.GetRecords<CsvDataModel>().ToList();
    }

    foreach (var r in records)
    {
        fields[r.Field].SetValue(r.Value);
    }

    // Close the document
    pdfDoc.Close();
    return true;
})
.WithName("PostPdf");


app.Run();


