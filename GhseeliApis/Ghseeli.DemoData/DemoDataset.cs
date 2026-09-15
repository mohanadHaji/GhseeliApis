using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Ghseeli.DemoData;

public static class DemoDataDefinition
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    public static DemoDataset Create()
    {
        var companies = CreateCompanies();
        var customers = CreateCustomers();
        var bookings = CreateBookings(companies, customers);

        return new DemoDataset(
            new DemoMetadata(
                "demo",
                "frontend-demo-v1",
                false,
                "DEMO DATA ONLY - fictional records isolated in the Demo partition; never use as real customer data.",
                BaseTime),
            CreateBusinessUsers(companies),
            companies,
            customers,
            CreateDrafts(companies, customers),
            bookings);
    }

    private static List<DemoCompany> CreateCompanies()
    {
        var company1 = new DemoCompany(
            Id('c', 1),
            "[DEMO] Sparkle Mobile Wash",
            "[DEMO] سباركل للغسيل التجريبي",
            "[DEMO] ספארקל שטיפה ניסיונית",
            11,
            [
                Branch(1, 1, "[DEMO] Ramallah Center", "[DEMO] فرع رام الله التجريبي", "[DEMO] סניף רמאללה ניסיוני", 31.9038, 35.2034),
                Branch(2, 1, "[DEMO] Al-Bireh North", "[DEMO] فرع البيرة التجريبي", "[DEMO] סניף אל-בירה ניסיוני", 31.9100, 35.2160)
            ],
            [
                Category(1, 1, "Exterior", "غسيل خارجي تجريبي", "שטיפה חיצונית ניסיונית"),
                Category(2, 1, "Interior", "تنظيف داخلي تجريبي", "ניקוי פנימי ניסיוני")
            ],
            [
                Offering(1, 1, 1, 1, "Quick Exterior", "غسيل خارجي سريع تجريبي", "שטיפה חיצונית מהירה ניסיונית", 25m, 25,
                    Group(1, 1, "Vehicle size", "حجم المركبة", "גודל הרכב", "SingleChoice", true, 1, 1,
                        Choice(1, 1, "Sedan", "سيدان", "סדאן", 0m, 0),
                        Choice(2, 1, "SUV", "دفع رباعي", "רכב שטח", 8m, 10))),
                Offering(2, 1, 1, 1, "Premium Exterior", "غسيل خارجي فاخر تجريبي", "שטיפה חיצונית פרימיום ניסיונית", 45m, 40,
                    Group(2, 2, "Finish", "طبقة الحماية", "שכבת גימור", "MultipleChoice", false, 0, 2,
                        Choice(3, 2, "Wax", "شمع", "ווקס", 12m, 10),
                        Choice(4, 2, "Tire shine", "تلميع الإطارات", "הברקת צמיגים", 6m, 5),
                        Choice(5, 2, "Rain repellent", "طارد المطر", "דוחה גשם", 9m, 5))),
                Offering(3, 1, 2, 2, "Interior Refresh", "تنظيف داخلي خفيف تجريبي", "רענון פנימי ניסיוני", 35m, 35),
                Offering(4, 1, 2, 2, "Deep Interior", "تنظيف داخلي عميق تجريبي", "ניקוי פנימי עמוק ניסיוני", 70m, 75,
                    Group(3, 4, "Seat rows", "صفوف المقاعد", "שורות מושבים", "QuantityCounter", true, 1, 3,
                        Choice(6, 3, "Seat row", "صف مقاعد", "שורת מושבים", 10m, 10),
                        Choice(7, 3, "Child seat", "مقعد أطفال", "מושב ילדים", 7m, 8),
                        Choice(8, 3, "Pet hair", "إزالة شعر الحيوانات", "הסרת שיער בעלי חיים", 15m, 15)))
            ]);

        var company2 = new DemoCompany(
            Id('c', 2),
            "[DEMO] Blue Wave Auto Care",
            "[DEMO] الموجة الزرقاء التجريبية",
            "[DEMO] הגל הכחול ניסיוני",
            7,
            [
                Branch(3, 2, "[DEMO] Nablus East", "[DEMO] فرع نابلس التجريبي", "[DEMO] סניף שכם ניסיוני", 32.2211, 35.2544),
                Branch(4, 2, "[DEMO] Tulkarm", "[DEMO] فرع طولكرم التجريبي", "[DEMO] סניף טולכרם ניסיוני", 32.3104, 35.0286)
            ],
            [
                Category(3, 2, "Detailing", "عناية وتلميع تجريبي", "דיטיילינג ניסיוני")
            ],
            [
                Offering(5, 2, 3, 3, "Express Detail", "عناية سريعة تجريبية", "דיטיילינג מהיר ניסיוני", 65m, 60),
                Offering(6, 2, 3, 3, "Full Detail", "عناية كاملة تجريبية", "דיטיילינג מלא ניסיוני", 140m, 150,
                    Group(4, 6, "Paint protection", "حماية الطلاء", "הגנת צבע", "SegmentedSingleButtonChoice", false, 0, 1,
                        Choice(9, 4, "Standard sealant", "حماية عادية", "איטום רגיל", 20m, 15),
                        Choice(10, 4, "Ceramic spray", "رذاذ سيراميك", "תרסיס קרמי", 45m, 25),
                        Choice(11, 4, "Ceramic coating", "طلاء سيراميك", "ציפוי קרמי", 180m, 120))),
                Offering(7, 2, 3, 4, "Headlight Polish", "تلميع المصابيح تجريبي", "ליטוש פנסים ניסיוני", 40m, 35),
                Offering(8, 2, 3, 4, "Engine Bay Clean", "تنظيف حجرة المحرك تجريبي", "ניקוי תא מנוע ניסיוני", 55m, 45,
                    Group(5, 8, "Included inspection", "فحص مشمول", "בדיקה כלולה", "FixedIncludedChoice", true, 1, 1,
                        Choice(12, 5, "Visual inspection", "فحص بصري", "בדיקה חזותית", 0m, 5),
                        Choice(13, 5, "Protective dressing", "طبقة حماية", "חומר הגנה", 10m, 5)))
            ]);

        var company3 = new DemoCompany(
            Id('c', 3),
            "[DEMO] Green Garage Wash",
            "[DEMO] مغسلة المرآب الأخضر التجريبية",
            "[DEMO] מוסך ירוק ניסיוני",
            4,
            [
                Branch(5, 3, "[DEMO] Hebron South", "[DEMO] فرع الخليل التجريبي", "[DEMO] סניף חברון ניסיוני", 31.5326, 35.0998)
            ],
            [
                Category(4, 3, "Eco Wash", "غسيل صديق للبيئة تجريبي", "שטיפה אקולוגית ניסיונית")
            ],
            [
                Offering(9, 3, 4, 5, "Waterless Wash", "غسيل بدون ماء تجريبي", "שטיפה ללא מים ניסיונית", 38m, 35),
                Offering(10, 3, 4, 5, "Steam Clean", "تنظيف بالبخار تجريبي", "ניקוי בקיטור ניסיוני", 58m, 50,
                    Group(6, 10, "Fragrance", "الرائحة", "ריח", "SingleChoice", false, 0, 1,
                        Choice(14, 6, "No fragrance", "بدون رائحة", "ללא ריח", 0m, 0),
                        Choice(15, 6, "Citrus", "حمضيات", "הדרים", 3m, 0),
                        Choice(16, 6, "Ocean", "محيط", "אוקיינוס", 3m, 0),
                        Choice(17, 6, "New car", "سيارة جديدة", "רכב חדש", 3m, 0),
                        Choice(18, 6, "Lavender", "لافندر", "לבנדר", 3m, 0))),
                Offering(11, 3, 4, 5, "Fleet Basic", "غسيل أسطول أساسي تجريبي", "שטיפת צי בסיסית ניסיונית", 22m, 20),
                Offering(12, 3, 4, 5, "Motorcycle Care", "عناية دراجة نارية تجريبية", "טיפול באופנוע ניסיוני", 30m, 30)
            ]);

        var company4 = new DemoCompany(
            Id('c', 4),
            "[DEMO] City Shine Express",
            "[DEMO] سيتي شاين إكسبرس التجريبية",
            "[DEMO] סיטי שיין אקספרס ניסיוני",
            9,
            [
                Branch(6, 4, "[DEMO] Jenin Center", "[DEMO] فرع جنين التجريبي", "[DEMO] סניף ג'נין ניסיוני", 32.4618, 35.3009),
                Branch(7, 4, "[DEMO] Qalqilya West", "[DEMO] فرع قلقيلية التجريبي", "[DEMO] סניף קלקיליה ניסיוני", 32.1892, 34.9706)
            ],
            [
                Category(5, 4, "Express Care", "عناية سريعة تجريبية", "טיפול מהיר ניסיוני")
            ],
            [
                Offering(13, 4, 5, 6, "Express Wash", "غسيل سريع تجريبي", "שטיפה מהירה ניסיונית", 22m, 20),
                Offering(14, 4, 5, 6, "Wash and Vacuum", "غسيل وشفط تجريبي", "שטיפה ושאיבה ניסיונית", 42m, 40,
                    Group(7, 14, "Vacuum level", "مستوى الشفط", "רמת שאיבה", "SingleChoice", true, 1, 1,
                        Choice(19, 7, "Standard", "عادي", "רגיל", 0m, 0),
                        Choice(20, 7, "Deep", "عميق", "עמוק", 10m, 10),
                        Choice(21, 7, "Pet hair", "شعر حيوانات", "שיער בעלי חיים", 18m, 15))),
                Offering(15, 4, 5, 7, "Dashboard Care", "عناية لوحة القيادة تجريبية", "טיפול בלוח מחוונים ניסיוני", 28m, 25),
                Offering(16, 4, 5, 7, "Family Car Package", "باقة السيارة العائلية التجريبية", "חבילת רכב משפחתי ניסיונית", 82m, 85,
                    Group(8, 16, "Family extras", "إضافات عائلية", "תוספות משפחתיות", "MultipleChoice", false, 0, 3,
                        Choice(22, 8, "Child seat clean", "تنظيف مقعد طفل", "ניקוי מושב ילד", 8m, 10),
                        Choice(23, 8, "Trunk vacuum", "شفط الصندوق", "שאיבת תא מטען", 7m, 8),
                        Choice(24, 8, "Sanitizing", "تعقيم", "חיטוי", 12m, 10)))
            ]);

        var company5 = new DemoCompany(
            Id('c', 5),
            "[DEMO] Royal Auto Spa",
            "[DEMO] رويال أوتو سبا التجريبية",
            "[DEMO] רויאל אוטו ספא ניסיוני",
            6,
            [
                Branch(8, 5, "[DEMO] Bethlehem", "[DEMO] فرع بيت لحم التجريبي", "[DEMO] סניף בית לחם ניסיוני", 31.7054, 35.2024)
            ],
            [
                Category(6, 5, "Premium Care", "عناية فاخرة تجريبية", "טיפול פרימיום ניסיוני")
            ],
            [
                Offering(17, 5, 6, 8, "Executive Wash", "غسيل تنفيذي تجريبي", "שטיפה מנהלים ניסיונית", 75m, 70),
                Offering(18, 5, 6, 8, "Leather Treatment", "معالجة جلد تجريبية", "טיפול עור ניסיוני", 95m, 80,
                    Group(9, 18, "Leather condition", "حالة الجلد", "מצב העור", "SegmentedSingleButtonChoice", true, 1, 1,
                        Choice(25, 9, "Light care", "عناية خفيفة", "טיפול קל", 0m, 0),
                        Choice(26, 9, "Conditioning", "ترطيب", "ריכוך", 20m, 15),
                        Choice(27, 9, "Restoration", "ترميم", "שיקום", 55m, 40))),
                Offering(19, 5, 6, 8, "Paint Correction", "تصحيح طلاء تجريبي", "תיקון צבע ניסיוני", 210m, 240),
                Offering(20, 5, 6, 8, "Event Ready Package", "باقة جاهزية للمناسبات التجريبية", "חבילת הכנה לאירוע ניסיונית", 165m, 170,
                    Group(10, 20, "Final finish", "اللمسة النهائية", "גימור סופי", "SingleChoice", true, 1, 1,
                        Choice(28, 10, "Gloss", "لامع", "מבריק", 0m, 0),
                        Choice(29, 10, "Satin", "ساتان", "סאטן", 10m, 5),
                        Choice(30, 10, "Show finish", "لمسة عرض", "גימור תצוגה", 30m, 20)))
            ]);

        return [company1, company2, company3, company4, company5];
    }

    private static List<DemoCustomer> CreateCustomers() =>
    [
        Customer(1, "Maya Demo", "maya.demo@example.test", "+972555000101", 2, 2, 1),
        Customer(2, "Omar Demo", "omar.demo@example.test", "+972555000102", 1, 2, 2),
        Customer(3, "Lina Demo", "lina.demo@example.test", "+972555000103", 1, 2, 2),
        Customer(4, "Sami Demo", "sami.demo@example.test", "+972555000104", 1, 1, 1),
        Customer(5, "Noor Demo", "noor.demo@example.test", "+972555000105", 1, 1, 1),
        Customer(6, "Yousef Demo", "yousef.demo@example.test", "+972555000106", 2, 2, 2),
        Customer(7, "Rana Demo", "rana.demo@example.test", "+972555000107", 1, 2, 1),
        Customer(8, "Adam Demo", "adam.demo@example.test", "+972555000108", 1, 1, 2)
    ];

    private static List<DemoBusinessUser> CreateBusinessUsers(IReadOnlyList<DemoCompany> companies) =>
    [
        new(Id('q', 1), "[DEMO] Sparkle Owner", "owner.sparkle@example.test", "Demo123!", "Owner", companies[0].Id, null),
        new(Id('q', 2), "[DEMO] Blue Wave Owner", "owner.bluewave@example.test", "Demo123!", "Owner", companies[1].Id, null),
        new(Id('q', 3), "[DEMO] Green Garage Owner", "owner.green@example.test", "Demo123!", "Owner", companies[2].Id, null),
        new(Id('q', 4), "[DEMO] Ramallah Employee", "employee.ramallah@example.test", "Demo123!", "Employee", companies[0].Id, companies[0].Branches[0].Id),
        new(Id('q', 5), "[DEMO] Nablus Employee", "employee.nablus@example.test", "Demo123!", "Employee", companies[1].Id, companies[1].Branches[0].Id),
        new(Id('q', 6), "[DEMO] City Shine Owner", "owner.cityshine@example.test", "Demo123!", "Owner", companies[3].Id, null),
        new(Id('q', 7), "[DEMO] Royal Auto Spa Owner", "owner.royal@example.test", "Demo123!", "Owner", companies[4].Id, null),
        new(Id('q', 8), "[DEMO] Jenin Employee", "employee.jenin@example.test", "Demo123!", "Employee", companies[3].Id, companies[3].Branches[0].Id)
    ];

    private static List<DemoDraft> CreateDrafts(
        IReadOnlyList<DemoCompany> companies,
        IReadOnlyList<DemoCustomer> customers)
    {
        var offeringIds = companies.SelectMany(company => company.Offerings).Select(offering => offering.Id).ToArray();
        return
        [
            Draft(1, customers[0].Devices[0].Id, companies[0].Id, companies[0].Branches[0].Id, "priced", false, BaseTime.AddDays(1), offeringIds[0], offeringIds[2]),
            Draft(2, customers[1].Devices[0].Id, companies[1].Id, companies[1].Branches[0].Id, "needsReprice", true, BaseTime.AddDays(2), offeringIds[5]),
            Draft(3, customers[2].Devices[0].Id, companies[2].Id, companies[2].Branches[0].Id, "active", true, BaseTime.AddDays(3), offeringIds[9]),
            Draft(4, customers[3].Devices[0].Id, companies[0].Id, companies[0].Branches[1].Id, "expired", true, BaseTime.AddDays(-2), offeringIds[3]),
            Draft(5, customers[5].Devices[0].Id, companies[3].Id, companies[3].Branches[0].Id, "priced", false, BaseTime.AddDays(4), offeringIds[13], offeringIds[15]),
            Draft(6, customers[7].Devices[0].Id, companies[4].Id, companies[4].Branches[0].Id, "active", true, BaseTime.AddDays(5), offeringIds[17])
        ];
    }

    private static List<DemoBooking> CreateBookings(
        IReadOnlyList<DemoCompany> companies,
        IReadOnlyList<DemoCustomer> customers)
    {
        var statuses = new[] { "Pending", "Confirmed", "InProgress", "Completed", "Cancelled", "NoShow", "Completed", "Confirmed", "Pending", "InProgress", "Completed", "Cancelled" };
        var paymentStatuses = new string?[] { null, "Pending", null, "Completed", "Failed", null, "Refunded", "Completed", "Pending", null, "Completed", "Failed" };
        var result = new List<DemoBooking>();
        for (var index = 0; index < statuses.Length; index++)
        {
            var company = companies[index % companies.Count];
            var branch = company.Branches[index % company.Branches.Count];
            var customer = customers[index % customers.Count];
            var offering = company.Offerings[index % company.Offerings.Count];
            var baseAmount = offering.BasePrice;
            var serviceFee = 5m;
            var tax = decimal.Round((baseAmount + serviceFee) * 0.16m, 2);
            var total = baseAmount + serviceFee + tax;
            var paymentStatus = paymentStatuses[index];
            result.Add(new DemoBooking(
                Id('k', index + 1),
                Id('r', index + 1),
                Id('w', index + 1),
                Id('o', index + 101),
                customer.Id,
                customer.Devices[0].Id,
                company.Id,
                branch.Id,
                statuses[index],
                BaseTime.AddDays(index - 4),
                "ILS",
                baseAmount,
                serviceFee,
                tax,
                total,
                [new DemoBookingItem(offering.Id, offering.NameAr, offering.NameHe, baseAmount, offering.DurationMinutes, [])],
                paymentStatus is null
                    ? null
                    : new DemoPayment(
                        Id('p', index + 1),
                        paymentStatus,
                        total,
                        "ILS",
                        $"DEMO-LAHZA-{index + 1:000}",
                        paymentStatus is "Completed" or "Refunded" ? $"DEMO-TXN-{index + 1:000}" : null)));
        }

        return result;
    }

    private static DemoCustomer Customer(
        int number,
        string name,
        string email,
        string phone,
        int deviceCount,
        int vehicleCount,
        int addressCount)
    {
        var devices = Enumerable.Range(1, deviceCount)
            .Select(index => new DemoDevice(
                Id('d', number * 10 + index),
                Id('i', number * 10 + index),
                index % 2 == 0 ? "ios" : "android",
                $"1.{number}.{index}",
                Token(number, index),
                true))
            .ToList();
        var vehicles = Enumerable.Range(1, vehicleCount)
            .Select(index => new DemoVehicle(
                Id('v', number * 10 + index),
                index % 2 == 0 ? "Toyota" : "Hyundai",
                index % 2 == 0 ? "Corolla" : "Tucson",
                (2020 + ((number + index) % 6)).ToString(),
                $"DEMO-{number}{index:00}",
                index % 2 == 0 ? "White" : "Blue"))
            .ToList();
        var addresses = Enumerable.Range(1, addressCount)
            .Select(index => new DemoAddress(
                Id('a', 500 + number * 10 + index),
                $"[DEMO] Street {number}, Building {index}",
                number % 2 == 0 ? "Ramallah" : "Al-Bireh",
                "Demo Area",
                31.90 + number / 1000d,
                35.20 + index / 1000d,
                index == 1))
            .ToList();

        return new DemoCustomer(Id('u', number), $"[DEMO] {name}", email, "Demo123!", phone, devices, vehicles, addresses);
    }

    private static DemoBranch Branch(int number, int companyNumber, string nameEn, string nameAr, string nameHe, double latitude, double longitude) =>
        new(Id('b', number), Id('c', companyNumber), nameEn, nameAr, nameHe, $"[DEMO] Address {number}", $"عنوان تجريبي {number}", $"כתובת ניסיונית {number}", latitude, longitude, 18 + number, "Asia/Jerusalem");

    private static DemoCategory Category(int number, int companyNumber, string nameEn, string nameAr, string nameHe) =>
        new(Id('g', number), Id('c', companyNumber), nameEn, nameAr, nameHe);

    private static DemoOffering Offering(
        int number,
        int companyNumber,
        int categoryNumber,
        int branchNumber,
        string nameEn,
        string nameAr,
        string nameHe,
        decimal basePrice,
        int durationMinutes,
        params DemoAddonGroup[] groups) =>
        new(Id('e', number), Id('c', companyNumber), Id('g', categoryNumber), Id('b', branchNumber), $"DEMO-SVC-{number:000}", nameEn, nameAr, nameHe, basePrice, durationMinutes, groups.ToList());

    private static DemoAddonGroup Group(
        int number,
        int offeringNumber,
        string nameEn,
        string nameAr,
        string nameHe,
        string selectionType,
        bool required,
        int minimum,
        int? maximum,
        params DemoAddonChoice[] choices) =>
        new(Id('h', number), Id('e', offeringNumber), nameEn, nameAr, nameHe, selectionType, required, minimum, maximum, choices.ToList());

    private static DemoAddonChoice Choice(
        int number,
        int groupNumber,
        string nameEn,
        string nameAr,
        string nameHe,
        decimal price,
        int duration) =>
        new(Id('x', number), Id('h', groupNumber), nameEn, nameAr, nameHe, price, duration, 1);

    private static DemoDraft Draft(
        int number,
        Guid deviceId,
        Guid companyId,
        Guid branchId,
        string state,
        bool requiresReprice,
        DateTimeOffset slot,
        params Guid[] offeringIds) =>
        new(
            Id('t', number),
            Id('o', number),
            deviceId,
            companyId,
            branchId,
            state,
            requiresReprice,
            slot,
            slot.AddDays(state == "expired" ? -1 : 2),
            offeringIds.Select((offeringId, index) => new DemoDraftItem(offeringId, index + 1, [])).ToList());

    private static Guid Id(char prefix, int number)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ghseeli-demo-{prefix}-{number}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string Token(int customerNumber, int deviceNumber) =>
        WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes($"ghseeli-demo-device-token-{customerNumber}-{deviceNumber}")));
}

public static class DemoDataJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(DemoDataset dataset) =>
        JsonSerializer.Serialize(dataset, Options) + Environment.NewLine;
}

public sealed record DemoDataset(
    DemoMetadata Metadata,
    List<DemoBusinessUser> BusinessUsers,
    List<DemoCompany> Companies,
    List<DemoCustomer> Customers,
    List<DemoDraft> Drafts,
    List<DemoBooking> Bookings);

public sealed record DemoMetadata(
    string DatasetType,
    string Version,
    bool LocalDevelopmentOnly,
    string Warning,
    DateTimeOffset GeneratedAtUtc);

public sealed record DemoCompany(
    Guid Id,
    string NameEn,
    string NameAr,
    string NameHe,
    long CatalogVersion,
    List<DemoBranch> Branches,
    List<DemoCategory> Categories,
    List<DemoOffering> Offerings);

public sealed record DemoBranch(
    Guid Id,
    Guid CompanyId,
    string NameEn,
    string NameAr,
    string NameHe,
    string AddressEn,
    string AddressAr,
    string AddressHe,
    double Latitude,
    double Longitude,
    double ServiceRadiusKm,
    string TimeZoneId);

public sealed record DemoCategory(Guid Id, Guid CompanyId, string NameEn, string NameAr, string NameHe);

public sealed record DemoOffering(
    Guid Id,
    Guid CompanyId,
    Guid CategoryId,
    Guid BranchId,
    string ReferenceCode,
    string NameEn,
    string NameAr,
    string NameHe,
    decimal BasePrice,
    int DurationMinutes,
    List<DemoAddonGroup> AddonGroups);

public sealed record DemoAddonGroup(
    Guid Id,
    Guid OfferingId,
    string NameEn,
    string NameAr,
    string NameHe,
    string SelectionType,
    bool IsRequired,
    int MinimumSelections,
    int? MaximumSelections,
    List<DemoAddonChoice> Choices);

public sealed record DemoAddonChoice(
    Guid Id,
    Guid AddonGroupId,
    string NameEn,
    string NameAr,
    string NameHe,
    decimal PriceAdjustment,
    int DurationAdjustmentMinutes,
    int DefaultQuantity);

public sealed record DemoCustomer(
    Guid Id,
    string FullName,
    string Email,
    string Password,
    string Phone,
    List<DemoDevice> Devices,
    List<DemoVehicle> Vehicles,
    List<DemoAddress> Addresses);

public sealed record DemoDevice(Guid Id, Guid InstallationId, string Platform, string AppVersion, string Token, bool IsActive);
public sealed record DemoBusinessUser(
    Guid Id,
    string FullName,
    string Email,
    string Password,
    string Role,
    Guid CompanyId,
    Guid? BranchId);
public sealed record DemoVehicle(Guid Id, string Make, string Model, string Year, string LicensePlate, string Color);
public sealed record DemoAddress(Guid Id, string AddressLine, string City, string Area, double Latitude, double Longitude, bool IsPrimary);

public sealed record DemoDraft(
    Guid Id,
    Guid OrderGuid,
    Guid DeviceId,
    Guid CompanyId,
    Guid BranchId,
    string State,
    bool RequiresReprice,
    DateTimeOffset RequestedSlotStartUtc,
    DateTimeOffset ExpiresAtUtc,
    List<DemoDraftItem> Items);

public sealed record DemoDraftItem(Guid OfferingId, int Quantity, List<DemoSelection> Selections);
public sealed record DemoSelection(Guid AddonGroupId, Guid AddonChoiceId, int Quantity);

public sealed record DemoBooking(
    Guid CustomerReferenceId,
    Guid BusinessReservationId,
    Guid BusinessWorkOrderId,
    Guid OrderGuid,
    Guid CustomerId,
    Guid DeviceId,
    Guid CompanyId,
    Guid BranchId,
    string Status,
    DateTimeOffset RequestedSlotStartUtc,
    string Currency,
    decimal ItemSubtotal,
    decimal ServiceFee,
    decimal Tax,
    decimal GrandTotal,
    List<DemoBookingItem> Items,
    DemoPayment? Payment);

public sealed record DemoBookingItem(
    Guid OfferingId,
    string NameAr,
    string NameHe,
    decimal ItemSubtotal,
    int DurationMinutes,
    List<DemoSelection> Selections);

public sealed record DemoPayment(
    Guid Id,
    string Status,
    decimal Amount,
    string Currency,
    string ProviderReference,
    string? ProviderTransactionId);
