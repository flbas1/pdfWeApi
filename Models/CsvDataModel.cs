public interface ICsvDataModel
{
    // Define common properties or methods for your data models

}


public class CsvDataModelField : ICsvDataModel
{
    public string Field { get ; set;  }
    public string Section { get; set; }
    public string Notes { get; set; }
    public string PdfPage { get; set; }
    public string DataType { get; set; }
    
}

public class CsvDataModelValue : ICsvDataModel
{
    public string Field { get ; set;  }
    public string Value { get; set; }
    
}