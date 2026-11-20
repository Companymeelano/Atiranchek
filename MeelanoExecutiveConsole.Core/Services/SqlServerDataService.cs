using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using MeelanoExecutiveConsole.Models;

namespace MeelanoExecutiveConsole.Services;

/// <summary>
/// Short-lived SQL connection settings. These settings are created from the connection
/// dialog and are never persisted by this application. Passwords are used only in memory.
/// </summary>
public sealed class SqlServerConnectionSettings
{
    private readonly string? userName;
    private readonly string? password;

    public SqlServerConnectionSettings(
        string serverHost,
        int port,
        bool useWindowsAuthentication,
        string? userName,
        string? password)
    {
        ServerHost = NormalizeHost(serverHost);
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "شمارهٔ پورت باید بین ۱ و ۶۵۵۳۵ باشد.");

        Port = port;
        UseWindowsAuthentication = useWindowsAuthentication;
        this.userName = useWindowsAuthentication ? null : userName?.Trim();
        this.password = useWindowsAuthentication ? null : password;

        if (!useWindowsAuthentication && (string.IsNullOrWhiteSpace(this.userName) || string.IsNullOrEmpty(this.password)))
            throw new ArgumentException("برای ورود SQL، نام کاربری و رمز را در همین پنجره وارد کنید.");
    }

    public string ServerHost { get; }
    public int Port { get; }
    public bool UseWindowsAuthentication { get; }

    internal SqlConnection CreateConnection(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("نام پایگاه داده مشخص نشده است.", nameof(database));

        // Test-only: encrypt traffic, but do not authenticate the server certificate.
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{ServerHost},{Port}",
            InitialCatalog = database,
            IntegratedSecurity = UseWindowsAuthentication,
            ConnectTimeout = 8,
            Pooling = false,
            PersistSecurityInfo = false,
            ApplicationName = "MEELANO Executive Console",
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            // Routing hint only; access control still comes from SQL Server permissions.
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectRetryCount = 0
        };

        if (!UseWindowsAuthentication)
        {
            builder.UserID = userName!;
            builder.Password = password!;
        }

        return new SqlConnection(builder.ConnectionString);
    }

    private static string NormalizeHost(string serverHost)
    {
        var value = serverHost?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)
                                                         || character == ';' || character == ',' || character == '/' || character == (char)92))
        {
            throw new ArgumentException("IP یا نام میزبان معتبر وارد کنید؛ پورت را در کادر جداگانه بنویسید.", nameof(serverHost));
        }

        var addressText = value;
        if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
            addressText = value[1..^1];

        if (IPAddress.TryParse(addressText, out var address))
        {
            return address.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{address}]"
                : address.ToString();
        }

        // SQL named instances are intentionally not accepted here: the UI uses a fixed TCP port.
        if (value.Contains(':')
            || Uri.CheckHostName(value) != UriHostNameType.Dns
            || value.Length > 253
            || value.StartsWith(".", StringComparison.Ordinal)
            || value.EndsWith(".", StringComparison.Ordinal))
        {
            throw new ArgumentException("IP یا نام DNS معتبر وارد کنید.", nameof(serverHost));
        }

        return value;
    }
}

public sealed record SqlColumnMetadata(string Name, string DataType, bool IsNullable);

public sealed class SqlTableMetadata
{
    public SqlTableMetadata(string schema, string name, string objectType, IReadOnlyList<SqlColumnMetadata> columns)
    {
        Schema = schema;
        Name = name;
        ObjectType = objectType;
        Columns = columns;
    }

    public string Schema { get; }
    public string Name { get; }
    public string ObjectType { get; }
    public IReadOnlyList<SqlColumnMetadata> Columns { get; }
    public string DisplayName => $"[{Schema}].[{Name}] · {ObjectType}";
    public string ColumnsSummary => string.Join("، ", Columns.Select(column => column.Name));
}

public sealed record SqlValueOption(string RawValue, string LookupKey, bool IsNull)
{
    public string DisplayValue => IsNull
        ? "(SQL NULL)"
        : RawValue.Length == 0
            ? "(رشتهٔ خالی)"
            : RawValue == "(SQL NULL)" ? "«(SQL NULL)»" : RawValue;
    public const string NullLookupKey = "\0MEELANO_SQL_NULL\0";

    public static string KeyFor(object? value)
    {
        if (value is null or DBNull)
            return NullLookupKey;

        var raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return $"V{raw.Length.ToString(CultureInfo.InvariantCulture)}:{raw}";
    }
}

public sealed record SqlCheckImportMapping(
    SqlTableMetadata Table,
    string NumberColumn,
    string KindColumn,
    string PartyColumn,
    string AmountColumn,
    string DueDateColumn,
    string StatusColumn,
    string? BankColumn,
    string? RiskColumn,
    IReadOnlyDictionary<string, CheckKind> KindValues,
    IReadOnlyDictionary<string, string> StatusValues,
    decimal AmountMultiplierToToman,
    bool AutomaticValues = false);

public sealed record SqlSalesImportMapping(
    SqlTableMetadata Table,
    string DateColumn,
    string CustomerColumn,
    string SellerColumn,
    string AmountColumn,
    string ProfitColumn,
    string? CollectionStatusColumn,
    decimal AmountMultiplierToToman);

public sealed record SqlImportMapping(SqlCheckImportMapping Checks, SqlSalesImportMapping Sales);

public sealed record SqlServerProgress(int Percentage, string Stage, long RowsRead = 0, long TotalRows = 0);

/// <summary>Immutable, read-only snapshot loaded from the selected SQL tables/views.</summary>
public sealed class SqlServerRepository : IRepository
{
    private readonly IReadOnlyList<CheckItem> checks;
    private readonly IReadOnlyList<SaleItem> sales;

    internal SqlServerRepository(string database, IReadOnlyList<CheckItem> checks, IReadOnlyList<SaleItem> sales)
    {
        Database = database;
        this.checks = Array.AsReadOnly(checks.ToArray());
        this.sales = Array.AsReadOnly(sales.ToArray());
    }

    public string Database { get; }
    public IReadOnlyList<CheckItem> GetChecks() => checks;
    public IReadOnlyList<SaleItem> GetSales() => sales;
    public bool IsReadOnly => true;
    public string DataSourceDescription => $"snapshot SQL Server · {Database} · فقط‌خواندنی";
}

/// <summary>
/// Direct SQL Server reader. Only catalog/metadata and SELECT statements are issued;
/// identifiers always come from discovered metadata and are quoted before use.
/// </summary>
public sealed class SqlServerDataService
{
    public const int MaximumRowsPerTable = 250_000;
    private const int CommandTimeoutSeconds = 120;

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(
        SqlServerConnectionSettings settings,
        IProgress<SqlServerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        progress?.Report(new SqlServerProgress(8, "برقراری اتصال مستقیم به SQL Server…"));
        await using var connection = settings.CreateConnection("master");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new SqlServerProgress(70, "اتصال برقرار شد؛ دریافت پایگاه‌های در دسترس…"));

        const string sql = """
            SELECT [name]
            FROM sys.databases
            WHERE [database_id] > 4
              AND [state_desc] = N'ONLINE'
              AND HAS_DBACCESS([name]) = 1
            ORDER BY [name];
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        var databases = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(name))
                databases.Add(name);
        }

        progress?.Report(new SqlServerProgress(100, $"فهرست {databases.Count} پایگاه دادهٔ قابل‌دسترسی آماده شد."));
        return databases.AsReadOnly();
    }

    public async Task<IReadOnlyList<SqlTableMetadata>> DiscoverSchemaAsync(
        SqlServerConnectionSettings settings,
        string database,
        IProgress<SqlServerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        progress?.Report(new SqlServerProgress(8, "اتصال به پایگاه دادهٔ انتخاب‌شده…"));
        await using var connection = settings.CreateConnection(database);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new SqlServerProgress(50, "کشف جدول‌ها، نماها و ستون‌های قابل‌دسترسی…"));

        const string sql = """
            SELECT t.TABLE_SCHEMA, t.TABLE_NAME, t.TABLE_TYPE,
                   c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
            FROM INFORMATION_SCHEMA.TABLES AS t
            INNER JOIN INFORMATION_SCHEMA.COLUMNS AS c
              ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
            WHERE t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
              AND t.TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
            ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        var objects = new Dictionary<(string Schema, string Name), (string Type, List<SqlColumnMetadata> Columns)>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            var type = reader.GetString(2);
            var key = (schema, name);
            if (!objects.TryGetValue(key, out var item))
                item = (type, []);
            item.Columns.Add(new SqlColumnMetadata(
                reader.GetString(3),
                reader.GetString(4),
                string.Equals(reader.GetString(5), "YES", StringComparison.OrdinalIgnoreCase)));
            objects[key] = item;
        }

        var result = objects
            .Select(pair => new SqlTableMetadata(pair.Key.Schema, pair.Key.Name, pair.Value.Type, pair.Value.Columns.AsReadOnly()))
            .OrderBy(item => item.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        progress?.Report(new SqlServerProgress(100, $"ساختار {result.Count} جدول/نما کشف شد."));
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<SqlValueOption>> GetDistinctValuesAsync(
        SqlServerConnectionSettings settings,
        string database,
        SqlTableMetadata table,
        string column,
        IProgress<SqlServerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateColumn(table, column);
        progress?.Report(new SqlServerProgress(10, $"خواندن مقادیر متمایز ستون «{column}»…"));
        await using var connection = settings.CreateConnection(database);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var quotedTable = QuoteTable(table);
        var quotedColumn = QuoteIdentifier(column);
        var sql = $"SELECT DISTINCT TOP (51) {quotedColumn} AS [SourceValue] FROM {quotedTable} ORDER BY [SourceValue];";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        var values = new List<SqlValueOption>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0))
            {
                values.Add(new SqlValueOption("", SqlValueOption.NullLookupKey, true));
                continue;
            }

            var raw = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
            values.Add(new SqlValueOption(raw, SqlValueOption.KeyFor(raw), false));
        }

        if (values.Count > 50)
            throw new InvalidOperationException("این ستون بیش از ۵۰ مقدار متمایز دارد؛ یک View گزارش با مقادیر محدودتر انتخاب کنید.");

        var unique = values
            .GroupBy(value => value.LookupKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        progress?.Report(new SqlServerProgress(100, $"{unique.Count} مقدار متمایز برای نگاشت آماده شد."));
        return unique.AsReadOnly();
    }

    public async Task<SqlServerRepository> ImportAsync(
        SqlServerConnectionSettings settings,
        string database,
        SqlImportMapping mapping,
        IProgress<SqlServerProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int maximumRowsPerTable = MaximumRowsPerTable)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(mapping);
        if (maximumRowsPerTable is < 1 or > MaximumRowsPerTable)
            throw new ArgumentOutOfRangeException(nameof(maximumRowsPerTable));
        ValidateMapping(mapping);
        progress?.Report(new SqlServerProgress(5, "برقراری اتصال و آماده‌سازی خواندن…"));
        await using var connection = settings.CreateConnection(database);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new SqlServerProgress(14, "بررسی حجم داده‌های انتخاب‌شده…"));

        var checksCount = await CountRowsAsync(connection, mapping.Checks.Table, cancellationToken).ConfigureAwait(false);
        var salesCount = await CountRowsAsync(connection, mapping.Sales.Table, cancellationToken).ConfigureAwait(false);
        EnsureWithinLimit("چک‌ها", checksCount, maximumRowsPerTable);
        EnsureWithinLimit("فروش", salesCount, maximumRowsPerTable);
        var total = checked(checksCount + salesCount);
        progress?.Report(new SqlServerProgress(20, $"{total.ToString("N0", CultureInfo.InvariantCulture)} ردیف برای خواندن شناسایی شد.", 0, total));

        long rowsRead = 0;
        var checks = await ReadChecksAsync(connection, mapping.Checks, checksCount, total,
            count =>
            {
                rowsRead = count;
                ReportReadProgress(progress, rowsRead, total, "بارگذاری چک‌ها");
            }, cancellationToken).ConfigureAwait(false);
        var sales = await ReadSalesAsync(connection, mapping.Sales, salesCount, total, checksCount,
            count =>
            {
                rowsRead = count;
                ReportReadProgress(progress, rowsRead, total, "بارگذاری فروش");
            }, cancellationToken).ConfigureAwait(false);

        progress?.Report(new SqlServerProgress(98, "اعتبارسنجی و آماده‌سازی نمای فقط‌خواندنی…", rowsRead, total));
        progress?.Report(new SqlServerProgress(100, "بارگذاری و اعتبارسنجی کامل شد.", total, total));
        return new SqlServerRepository(database, checks, sales);
    }

    private static async Task<long> CountRowsAsync(
        SqlConnection connection,
        SqlTableMetadata table,
        CancellationToken cancellationToken)
    {
        var sql = $"SELECT COUNT_BIG(*) FROM {QuoteTable(table)};";
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<List<CheckItem>> ReadChecksAsync(
        SqlConnection connection,
        SqlCheckImportMapping mapping,
        long rowCount,
        long totalRows,
        Action<long> onRead,
        CancellationToken cancellationToken)
    {
        var fields = new List<(string Column, string Alias)>
        {
            (mapping.NumberColumn, "__number"),
            (mapping.KindColumn, "__kind"),
            (mapping.PartyColumn, "__party"),
            (mapping.AmountColumn, "__amount"),
            (mapping.DueDateColumn, "__due"),
            (mapping.StatusColumn, "__status")
        };
        if (!string.IsNullOrWhiteSpace(mapping.BankColumn))
            fields.Add((mapping.BankColumn, "__bank"));
        if (!string.IsNullOrWhiteSpace(mapping.RiskColumn))
            fields.Add((mapping.RiskColumn, "__risk"));

        var sql = BuildSelect(mapping.Table, fields);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        var records = new List<CheckItem>((int)rowCount);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        long index = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            index++;
            var number = RequiredText(reader, "__number", index, "شمارهٔ چک");
            if (!identifiers.Add(number))
                throw new InvalidOperationException($"شمارهٔ چک تکراری در ردیف {index.ToString(CultureInfo.InvariantCulture)} دیده شد؛ داده بارگذاری نشد.");

            var rawKind = ReadValue(reader, "__kind");
            var kindKey = SqlValueOption.KeyFor(rawKind);
            if (!mapping.KindValues.TryGetValue(kindKey, out var kind)
                && !(mapping.AutomaticValues && rawKind is string kindText && SqlAutomaticMapping.TryKind(kindText, out kind)))
                throw new InvalidOperationException($"نوع چک در ردیف {index.ToString(CultureInfo.InvariantCulture)} ناشناخته یا NULL است؛ قرارداد نمای گزارش را بررسی کنید.");

            var rawStatus = ReadValue(reader, "__status");
            var statusKey = SqlValueOption.KeyFor(rawStatus);
            if (!mapping.StatusValues.TryGetValue(statusKey, out var status) && mapping.AutomaticValues && rawStatus is string statusText)
                status = SqlAutomaticMapping.Status(statusText);
            if (status is null || !CheckStatuses.All.Contains(status, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"وضعیت چک در ردیف {index.ToString(CultureInfo.InvariantCulture)} نگاشت نشده است.");
            }

            if ((kind == CheckKind.دریافتی && status == CheckStatuses.Paid)
                || (kind == CheckKind.پرداختی && status == CheckStatuses.Collected))
                throw new InvalidOperationException($"نوع و وضعیت چک در ردیف {index.ToString(CultureInfo.InvariantCulture)} ناسازگارند؛ داده بارگذاری نشد.");

            var amount = ReadDecimal(reader, "__amount", index, "مبلغ چک") * mapping.AmountMultiplierToToman;
            if (amount <= 0)
                throw new InvalidOperationException($"مبلغ چک در ردیف {index.ToString(CultureInfo.InvariantCulture)} باید بیشتر از صفر باشد.");
            if (!PersianDateParser.TryParse(ReadValue(reader, "__due"), out var dueDate))
                throw new InvalidOperationException($"تاریخ سررسید در ردیف {index.ToString(CultureInfo.InvariantCulture)} قابل‌تفسیر نیست؛ تاریخ میلادی SQL یا متن شمسی YYYY/MM/DD لازم است.");

            var party = RequiredText(reader, "__party", index, "طرف حساب");
            var hasBank = HasColumn(reader, "__bank");
            var bank = hasBank ? OptionalText(reader, "__bank") : "—";

            var hasRisk = HasColumn(reader, "__risk") && ReadValue(reader, "__risk") is not null;
            var risk = hasRisk ? ReadRisk(reader, "__risk", index) : 0;
            records.Add(new CheckItem
            {
                Number = number,
                Kind = kind,
                Party = party,
                Amount = amount,
                DueDate = dueDate,
                Status = status,
                Bank = string.IsNullOrWhiteSpace(bank) ? "—" : bank,
                Risk = risk,
                HasRiskScore = hasRisk
            });

            if (index % 128 == 0 || index == rowCount)
                onRead(index);
        }

        if (index != rowCount)
            throw new InvalidOperationException("تعداد ردیف‌های چک هنگام بارگذاری تغییر کرد؛ دوباره اتصال را اجرا کنید.");
        return records;
    }

    private static async Task<List<SaleItem>> ReadSalesAsync(
        SqlConnection connection,
        SqlSalesImportMapping mapping,
        long rowCount,
        long totalRows,
        long offset,
        Action<long> onRead,
        CancellationToken cancellationToken)
    {
        var fields = new List<(string Column, string Alias)>
        {
            (mapping.DateColumn, "__date"),
            (mapping.CustomerColumn, "__customer"),
            (mapping.SellerColumn, "__seller"),
            (mapping.AmountColumn, "__amount"),
            (mapping.ProfitColumn, "__profit")
        };
        if (!string.IsNullOrWhiteSpace(mapping.CollectionStatusColumn))
            fields.Add((mapping.CollectionStatusColumn, "__collection"));

        var sql = BuildSelect(mapping.Table, fields);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        var records = new List<SaleItem>((int)rowCount);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        long index = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            index++;
            if (!PersianDateParser.TryParse(ReadValue(reader, "__date"), out var date))
                throw new InvalidOperationException($"تاریخ فروش در ردیف {index.ToString(CultureInfo.InvariantCulture)} قابل‌تفسیر نیست؛ تاریخ میلادی SQL یا متن شمسی YYYY/MM/DD لازم است.");

            var customer = RequiredText(reader, "__customer", index, "مشتری");
            var seller = RequiredText(reader, "__seller", index, "فروشنده");
            var amount = ReadDecimal(reader, "__amount", index, "مبلغ فروش") * mapping.AmountMultiplierToToman;
            var profit = ReadDecimal(reader, "__profit", index, "سود فروش") * mapping.AmountMultiplierToToman;
            if (amount <= 0)
                throw new InvalidOperationException($"مبلغ فروش در ردیف {index.ToString(CultureInfo.InvariantCulture)} باید بیشتر از صفر باشد.");
            var collectionStatus = HasColumn(reader, "__collection")
                ? OptionalText(reader, "__collection")
                : "نامشخص";

            records.Add(new SaleItem
            {
                Date = date,
                Customer = customer,
                Seller = seller,
                Amount = amount,
                Profit = profit,
                CollectionStatus = string.IsNullOrWhiteSpace(collectionStatus) ? "نامشخص" : collectionStatus
            });

            if (index % 128 == 0 || index == rowCount)
                onRead(offset + index);
        }

        if (index != rowCount)
            throw new InvalidOperationException("تعداد ردیف‌های فروش هنگام بارگذاری تغییر کرد؛ دوباره اتصال را اجرا کنید.");
        return records;
    }

    private static bool HasColumn(SqlDataReader reader, string name)
    {
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            if (string.Equals(reader.GetName(ordinal), name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void ValidateMapping(SqlImportMapping mapping)
    {
        var check = mapping.Checks;
        ValidateColumn(check.Table, check.NumberColumn);
        ValidateColumn(check.Table, check.KindColumn);
        ValidateColumn(check.Table, check.PartyColumn);
        ValidateColumn(check.Table, check.AmountColumn);
        ValidateColumn(check.Table, check.DueDateColumn);
        ValidateColumn(check.Table, check.StatusColumn);
        if (!string.IsNullOrWhiteSpace(check.BankColumn)) ValidateColumn(check.Table, check.BankColumn);
        if (!string.IsNullOrWhiteSpace(check.RiskColumn)) ValidateColumn(check.Table, check.RiskColumn);
        if (check.AmountMultiplierToToman <= 0)
            throw new InvalidOperationException("ضریب تبدیل مبلغ چک به تومان باید مثبت باشد.");

        foreach (var pair in check.KindValues)
        {
            if (pair.Key.Length == 0 || !Enum.IsDefined(typeof(CheckKind), pair.Value))
                throw new InvalidOperationException("نگاشت نوع چک معتبر نیست.");
        }
        foreach (var pair in check.StatusValues)
        {
            if (pair.Key.Length == 0 || !CheckStatuses.All.Contains(pair.Value, StringComparer.Ordinal))
                throw new InvalidOperationException("نگاشت وضعیت چک معتبر نیست.");
        }

        var sale = mapping.Sales;
        ValidateColumn(sale.Table, sale.DateColumn);
        ValidateColumn(sale.Table, sale.CustomerColumn);
        ValidateColumn(sale.Table, sale.SellerColumn);
        ValidateColumn(sale.Table, sale.AmountColumn);
        ValidateColumn(sale.Table, sale.ProfitColumn);
        if (!string.IsNullOrWhiteSpace(sale.CollectionStatusColumn))
            ValidateColumn(sale.Table, sale.CollectionStatusColumn);
        if (sale.AmountMultiplierToToman <= 0)
            throw new InvalidOperationException("ضریب تبدیل مبلغ فروش به تومان باید مثبت باشد.");
    }

    private static void ValidateColumn(SqlTableMetadata table, string? column)
    {
        if (table is null || string.IsNullOrWhiteSpace(column)
            || !table.Columns.Any(item => string.Equals(item.Name, column, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("یکی از نگاشت‌های ستون با schema کشف‌شده سازگار نیست؛ ساختار را دوباره بارگذاری کنید.");
        }
    }

    private static string BuildSelect(SqlTableMetadata table, IReadOnlyList<(string Column, string Alias)> fields)
    {
        var selection = string.Join(", ", fields.Select(field =>
        {
            ValidateColumn(table, field.Column);
            return $"{QuoteIdentifier(field.Column)} AS {QuoteIdentifier(field.Alias)}";
        }));
        return $"SELECT {selection} FROM {QuoteTable(table)};";
    }

    private static string QuoteTable(SqlTableMetadata table) =>
        $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";

    private static string QuoteIdentifier(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static object? ReadValue(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
    }

    private static string RequiredText(SqlDataReader reader, string column, long row, string label)
    {
        var value = ReadValue(reader, column);
        var text = value is null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        if (text.Length == 0)
            throw new InvalidOperationException($"{label} در ردیف {row.ToString(CultureInfo.InvariantCulture)} خالی است.");
        return text;
    }

    private static string OptionalText(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return string.Empty;
        return Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static decimal ReadDecimal(SqlDataReader reader, string column, long row, string label)
    {
        var value = ReadValue(reader, column);
        if (value is null)
            throw new InvalidOperationException($"{label} در ردیف {row.ToString(CultureInfo.InvariantCulture)} خالی است.");
        if (TryConvertDecimal(value, out var amount))
            return amount;
        throw new InvalidOperationException($"{label} در ردیف {row.ToString(CultureInfo.InvariantCulture)} عدد معتبر نیست.");
    }

    private static int ReadRisk(SqlDataReader reader, string column, long row)
    {
        var value = ReadDecimal(reader, column, row, "امتیاز ریسک");
        if (value is < 0 or > 100 || decimal.Truncate(value) != value)
            throw new InvalidOperationException($"امتیاز ریسک در ردیف {row.ToString(CultureInfo.InvariantCulture)} باید عدد صحیح بین صفر تا صد باشد.");
        return decimal.ToInt32(value);
    }

    private static bool TryConvertDecimal(object value, out decimal result)
    {
        try
        {
            switch (value)
            {
                case decimal decimalValue:
                    result = decimalValue;
                    return true;
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                    result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                case string text:
                    var normalized = PersianText.NormalizeForSearch(text)
                        .Replace("٬", string.Empty, StringComparison.Ordinal)
                        .Replace("٫", ".", StringComparison.Ordinal)
                        .Trim();
                    var commaIndex = normalized.LastIndexOf(',');
                    var dotIndex = normalized.LastIndexOf('.');
                    if (commaIndex >= 0 && dotIndex >= 0)
                    {
                        normalized = commaIndex > dotIndex
                            ? normalized.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                            : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
                    }
                    else if (commaIndex >= 0)
                    {
                        var commaParts = normalized.Split(',');
                        normalized = commaParts.Length == 2 && commaParts[1].Length is > 0 and <= 2
                            ? normalized.Replace(',', '.')
                            : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
                    }
                    return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out result);
                default:
                    result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
            }
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static void EnsureWithinLimit(string label, long rowCount, int maximumRowsPerTable)
    {
        if (rowCount > maximumRowsPerTable)
        {
            throw new InvalidOperationException(
                $"تعداد ردیف‌های {label} از سقف محافظتی {maximumRowsPerTable.ToString("N0", CultureInfo.InvariantCulture)} بیشتر است؛ یک View گزارش محدودشده انتخاب کنید.");
        }
    }

    private static void ReportReadProgress(IProgress<SqlServerProgress>? progress, long rowsRead, long total, string stage)
    {
        var percentage = total <= 0
            ? 95
            : 20 + (int)Math.Min(75m, decimal.Floor((decimal)rowsRead / total * 75m));
        progress?.Report(new SqlServerProgress(Math.Clamp(percentage, 20, 95), stage, rowsRead, total));
    }
}

public static class SqlServerErrorMessages
{
    public static string ToPersian(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        SqlException? sqlException = null;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException currentSqlException)
                sqlException ??= currentSqlException;
        }

        if (sqlException is not null)
            return ToPersianSqlError(sqlException.Number);
        if (exception is InvalidOperationException or ArgumentException)
            return exception.Message;
        if (exception is OperationCanceledException)
            return "عملیات لغو شد.";
        if (exception is TimeoutException)
            return "مهلت عملیات تمام شد؛ شبکه و پاسخ‌گویی SQL Server را بررسی کنید.";
        return "عملیات پایگاه داده انجام نشد. تنظیمات اتصال و مجوز فقط‌خواندنی را بررسی کنید.";
    }

    private static string ToPersianSqlError(int errorNumber) => errorNumber switch
    {
        18456 => "ورود SQL ناموفق است. حساب کم‌دسترسی، نام کاربری و رمز واردشده را بررسی کنید.",
        4060 or 916 => "دسترسی به پایگاه داده رد شد؛ مجوز CONNECT و SELECT حساب را بررسی کنید.",
        -2 => "مهلت اتصال/خواندن تمام شد؛ IP، پورت، فایروال و در دسترس بودن SQL Server را بررسی کنید.",
        53 or 40 or 10060 or 10061 => "به SQL Server وصل نشد؛ IP، پورت ۵۰۵۰/۱۴۳۳، فایروال و port forwarding را بررسی کنید.",
        _ => $"SQL Server عملیات را نپذیرفت (کد خطا {errorNumber.ToString(CultureInfo.InvariantCulture)}). تنظیمات اتصال و دسترسی schema را بررسی کنید."
    };
}

/// <summary>Parses SQL DATE/DATETIME values as Gregorian and recognizable Persian text as Jalali.</summary>
public static class PersianDateParser
{
    private static readonly PersianCalendar Calendar = new();

    public static bool TryParse(object? value, out DateTime date)
    {
        date = default;
        if (value is null or DBNull)
            return false;
        if (value is DateTime dateTime)
            return TrySetSupportedDate(dateTime, out date);
        if (value is DateOnly dateOnly)
            return TrySetSupportedDate(dateOnly.ToDateTime(TimeOnly.MinValue), out date);
        if (value is DateTimeOffset dateTimeOffset)
            return TrySetSupportedDate(dateTimeOffset.Date, out date);

        var text = PersianText.NormalizeForSearch(Convert.ToString(value, CultureInfo.InvariantCulture))?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.Length == 8 && text.All(character => character is >= '0' and <= '9')
            && int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var compactYear)
            && int.TryParse(text.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var compactMonth)
            && int.TryParse(text.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var compactDay))
        {
            return TryBuildCalendarDate(compactYear, compactMonth, compactDay, out date);
        }

        var separator = text.FirstOrDefault(character => character is '/' or '-' or '.');
        if (separator == '\0' || text.Any(character => (character is '/' or '-' or '.') && character != separator))
            return false;

        var parts = text.Split(separator);
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            return false;
        }

        return TryBuildCalendarDate(year, month, day, out date);
    }

    private static bool TrySetSupportedDate(DateTime value, out DateTime date)
    {
        date = value.Date;
        if (date >= Calendar.MinSupportedDateTime.Date && date <= Calendar.MaxSupportedDateTime.Date)
            return true;
        date = default;
        return false;
    }

    private static bool TryBuildCalendarDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (year is >= 1300 and <= 1499)
        {
            try
            {
                date = Calendar.ToDateTime(year, month, day, 0, 0, 0, 0).Date;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        if (year is >= 1900 and <= 2100)
        {
            try
            {
                date = new DateTime(year, month, day);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return false;
    }
}
