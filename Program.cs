using System.Collections;
using System.Dynamic;
using System.Globalization;
using System.Text;
//using System.Text.Json;
using Newtonsoft.Json;
using CsvHelper;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using Org.BouncyCastle.Asn1.X509.Qualified;
using Org.BouncyCastle.Security;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddTransient<CsvDataModelField>();
builder.Services.AddTransient<CsvDataModelValue>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

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



app.MapGet("/data", (string csvFilePath, IServiceProvider serviceProvider) =>
{
    var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        Delimiter = ",",
        HeaderValidated = null,
        MissingFieldFound = null
    };

    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, config))
    {
        var records = new List<dynamic>();
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var record = new ExpandoObject() as IDictionary<string, Object>;
            foreach (var header in csv.HeaderRecord)
            {
                record[header] = csv.GetField(header);
            }
            records.Add(record);
        }
        return Results.Ok(records);
    }
})
.WithName("GetData");

//var csvLock = new object();
var csvLock = new SemaphoreSlim(1, 1);
app.MapPost("/data", async (HttpContext context, IServiceProvider serviceProvider) =>
{
    var csvFilePath = context.Request.Query["csvFilePath"].ToString();

    var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        Delimiter = ",",
        HeaderValidated = null,
        MissingFieldFound = null
    };

    await csvLock.WaitAsync();

    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, config))
    {
        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord;

        Type dataModelType = getDataModelType(headers, serviceProvider);

        // Read the incoming data
        var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var data = JsonConvert.DeserializeObject<object>(requestBody);

        var incomingRecords = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(dataModelType));

        if (data is Newtonsoft.Json.Linq.JArray)
        {
            // Deserialize as a list of records
            var records = JsonConvert.DeserializeObject(requestBody, typeof(List<>).MakeGenericType(dataModelType)) as IList;
            foreach (var record in records)
            {
                incomingRecords.Add(record);
            }
        }
        else
        {
            // Deserialize as a single record
            var singleRecord = JsonConvert.DeserializeObject(requestBody, dataModelType);
            incomingRecords.Add(singleRecord);
        }

        if (incomingRecords == null || incomingRecords.Count == 0) return Results.BadRequest();

        // lock (csvLock)
        {
            var records = csv.GetRecords(dataModelType).Cast<dynamic>().ToList();

            //foreach (var incomingRecord in incomingRecords)
            //{

            if (incomingRecords.Count > 1)  //delete the existing and rebuild
            {
                records.Clear();
                foreach (var incomingRecord in incomingRecords)
                {
                    records.Add(incomingRecord);
                }
            }
            else
            {
                dynamic incomingDynamic = incomingRecords[0];
                var existingRecord = records.FirstOrDefault(r => r.Field == incomingDynamic.Field);
                if (existingRecord != null)
                {
                    // Update the existing record
                    var index = records.IndexOf(existingRecord);
                    records[index] = incomingDynamic;
                }
                else
                {
                    // Add the new record
                    records.Add(incomingDynamic);
                }
            }

            using (var writer = new StreamWriter(csvFilePath))
            using (var csvWriter = new CsvWriter(writer, config))
            {
                csvWriter.WriteRecords(records);
            }
        }
        csvLock.Release();
        return Results.Ok("Data updated successfully");
    }
})
.WithName("PostData");



// app.MapGet("/data", (string csvFilePath, IServiceProvider serviceProvider) =>
// {
//     var records = new List<CsvDataModel>();

//     // var CsvConfig = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
//     // {
//     //     HasHeaderRecord = true,
//     //     Delimiter = ",",
//     //     HeaderValidated = null,
//     //     MissingFieldFound = null
//     // };
//     using (var reader = new StreamReader(csvFilePath))
//     using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
//     {
//         var retVal = csv.GetRecords<CsvDataModel>().ToList();
//         return retVal;
//     }
// })
// .WithName("GetData");

// var csvLock = new object();
// app.MapPost("/data", async (HttpContext context) =>
// {
//     var csvFilePath = context.Request.Query["csvFilePath"].ToString();
//     var data = await context.Request.ReadFromJsonAsync<CsvDataModel>();

//     if (data == null) return Results.BadRequest();

//     var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, Delimiter = "," };

//     lock (csvLock)
//     {
//         var records = new List<CsvDataModel>();
//         using (var reader = new StreamReader(csvFilePath))
//         using (var csv = new CsvReader(reader, config))
//         {
//             records = csv.GetRecords<CsvDataModel>().ToList();
//         }

//         var existingRecord = records.FirstOrDefault(r => r.Field == data.Field);
//         if (existingRecord != null)
//         {
//             existingRecord.Value = data.Value;
//         }

//         using (var writer = new StreamWriter(csvFilePath))
//         using (var csv = new CsvWriter(writer, config))
//         {
//             csv.WriteRecords(records);
//         }
//         return Results.Ok(true);
//     }
// })
// .WithName("PostData");


//todo:  this should be post.  post is idempotent
app.MapPost("/pdf", () =>
{
    string csvFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Values.csv");
    string inputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "902c10-21.pdf");
    string outputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "902c10-21_filled.pdf");

    // Open the existing PDF
    PdfDocument pdfDoc = new PdfDocument(new PdfReader(inputFilePath), new PdfWriter(outputFilePath));

    // Get the AcroForm from the PDF document
    PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, true);

    // Get all form fields
    var fields = form.GetFormFields();

    // Read field values from CSV file
    var records = new List<CsvDataModelValue>();
    using (var reader = new StreamReader(csvFilePath))
    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
    {
        records = csv.GetRecords<CsvDataModelValue>().ToList();
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
    string pdfTemplate = Path.Combine(Directory.GetCurrentDirectory(), "902c10-21.pdf");
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


Type getDataModelType(string[] headers, IServiceProvider serviceProvider)
{
    if (headers.Contains("Value"))
    {
        return typeof(CsvDataModelValue);
    }
    else
    {
        return typeof(CsvDataModelField);
    }
}