using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace TransforMagiX
{
    // Custom boolean converter to write lowercase boolean values
    public class LowerCaseBooleanConverter : BooleanConverter
    {
        public override string ConvertToString(object value, IWriterRow row, MemberMapData memberMapData)
        {
            if (value is bool boolValue)
            {
                return boolValue.ToString().ToLower();
            }
            return base.ConvertToString(value, row, memberMapData);
        }
    }
}
