using System.Collections;
using System.Globalization;
using System.Text;
using CsvHelper;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
        .WithTitle("Scalar API Reference")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.JQuery);
    });
}

// app.UseHttpsRedirection();  // to redirect to https

app.UseDefaultFiles();  // Add this line to serve default files like index.html
app.UseStaticFiles(new StaticFileOptions()   // to serve static files
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
        context.Context.Response.Headers.Append("Expires", "-1");
    }
});




app.MapGet("/data", (string csvFilePath) =>
{
    var records = new List<CsvDataModel>();
    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
    {
        var retVal = csv.GetRecords<CsvDataModel>().ToList();
        return retVal;
    }
})
.WithName("GetData");

var csvLock = new object();
app.MapPost("/data", async (HttpContext context) =>
{
    var csvFilePath = context.Request.Query["csvFilePath"].ToString();
    var data = await context.Request.ReadFromJsonAsync<CsvDataModel>();

    if (data == null) return Results.BadRequest();

    var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, Delimiter = "," };

    lock (csvLock)
    {
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
        }

        using (var writer = new StreamWriter(csvFilePath))
        using (var csv = new CsvWriter(writer, config))
        {
            csv.WriteRecords(records);
        }
        return Results.Ok(true);
    }
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
    var fields = form.GetFormFields();

    // Read field values from CSV file
    var records = new List<CsvDataModel>();
    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
    {
        records = csv.GetRecords<CsvDataModel>().ToList();
    }

    foreach (var r in records)
    {
        if (fields.ContainsKey(r.Field))  // sometimes a field needs to be there ... but the PDF is wrong ... like for a calculation
        {
            fields[r.Field].SetValue(r.Value);
        }
    }

    //  form.FlattenFields();

    // Close the document
    pdfDoc.Close();
    return true;
})
.WithName("PostPdf");

app.MapGet("/pdf/base64", async (HttpContext context) =>
{
    string pdfFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "902c10-21_filled.pdf");

    if (!System.IO.File.Exists(pdfFilePath))
    {
        return Results.NotFound();
    }

    byte[] pdfBytes = await System.IO.File.ReadAllBytesAsync(pdfFilePath);
    string base64String = Convert.ToBase64String(pdfBytes);

    return Results.Ok(base64String);
})
.WithName("GetPdfBase64");

app.MapPost("/renameFields", () =>
{
    string pdfTemplate = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "902c10-21_filled.pdf");
    string outputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "902c10-21_filled_renamed.pdf");

    // Open the existing PDF
    PdfDocument pdfDoc = new PdfDocument(new PdfReader(pdfTemplate), new PdfWriter(outputFilePath));
    PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, true);

    // Rename unnamed textboxes
    var fieldNames = form.GetFormFields();
    int unnamedCounter = 1;
    foreach (var field in fieldNames)
    {
        if (string.IsNullOrEmpty(field.Key))
        {
            string newFieldName = "UnnamedField" + unnamedCounter++;
            PdfFormField formField = field.Value;
            formField.SetFieldName(newFieldName);
        }
    }

    // Close the document
    pdfDoc.Close();

    return Results.Ok("Fields renamed successfully");
})
.WithName("RenameFields");

app.MapGet("/pdf/Fields", () =>
{
    string pdfTemplate = Path.Combine(Directory.GetCurrentDirectory(),  "902c10-21.pdf");
    PdfDocument pdfDoc = new PdfDocument(new PdfReader(pdfTemplate));
    PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, true);

    var fieldNames = new List<string>();
    foreach (var field in form.GetFormFields())
    {
        fieldNames.Add(field.Key);
    }

    pdfDoc.Close();

    return Results.Json(fieldNames);
})
.WithName("GetPdfFields");


app.Run();


