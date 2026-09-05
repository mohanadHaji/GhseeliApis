using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhseeliApis.Migrations
{
    /// <inheritdoc />
    public partial class InitialCustomerDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PendingEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeleteScheduledFor = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookingConfirmationAttempts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftVersion = table.Column<int>(type: "int", nullable: false),
                    ReservationRequestJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingConfirmationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProviders",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SnapshotGeneratedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessfulRefreshAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptedRefreshAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailedRefreshAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RefreshLeaseAcquiredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefreshLeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefreshLeaseToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDrafts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    PublicVersion = table.Column<int>(type: "int", nullable: false),
                    RequestedSlotStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VehicleType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LicensePlate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VehicleMake = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleColor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    City = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    RequiresReprice = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfirmationClaimedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmationClaimedVersion = table.Column<int>(type: "int", nullable: true),
                    ConfirmationBookingReference = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerConfigurations",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SupportEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    SupportPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LegalNoticeAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LegalNoticeHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PrivacyPolicyUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TermsOfServiceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsMaintenanceModeEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MaintenanceMessageAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MaintenanceMessageHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDevices",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerInternalIdempotencyRecords",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    OwnerToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ResponseBody = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInternalIdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerInternalServiceNonces",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInternalServiceNonces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "dbo",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "dbo",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "dbo",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBookings",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedDraftVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BusinessStatusSequence = table.Column<long>(type: "bigint", nullable: false),
                    StatusChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedSlotStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedSlotEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProviderNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProviderNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BranchNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VehicleType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LicensePlate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VehicleMake = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleModel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VehicleColor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    City = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFeeMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ServiceFeeFlatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFeePercentageRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxableSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxAppliesToServiceFee = table.Column<bool>(type: "bit", nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PaymentState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Unpaid"),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    QuotedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBookings", x => x.Id);
                    table.CheckConstraint("CK_CustomerBookings_PaymentState", "[PaymentState] IN ('Unpaid','Pending','Completed','Failed','Refunded')");
                    table.ForeignKey(
                        name: "FK_CustomerBookings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserAddresses",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    City = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAddresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Make = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Year = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LicensePlate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogBranches",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AddressHe = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    HasPublishedServiceArea = table.Column<bool>(type: "bit", nullable: false),
                    UsesBranchCoordinates = table.Column<bool>(type: "bit", nullable: false),
                    ServiceAreaCenterLatitude = table.Column<double>(type: "float", nullable: true),
                    ServiceAreaCenterLongitude = table.Column<double>(type: "float", nullable: true),
                    ServiceAreaRadiusKm = table.Column<double>(type: "float", nullable: true),
                    AvailabilitySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogBranches_CatalogProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "dbo",
                        principalTable: "CatalogProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogCategories",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogCategories_CatalogProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "dbo",
                        principalTable: "CatalogProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftItems",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftItems_CheckoutDrafts_CheckoutDraftId",
                        column: x => x.CheckoutDraftId,
                        principalSchema: "dbo",
                        principalTable: "CheckoutDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftPricingSnapshots",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    QuotedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFeeMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ServiceFeeFlatAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceFeePercentageRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxableSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxAppliesToServiceFee = table.Column<bool>(type: "bit", nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftPricingSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftPricingSnapshots_CheckoutDrafts_CheckoutDraftId",
                        column: x => x.CheckoutDraftId,
                        principalSchema: "dbo",
                        principalTable: "CheckoutDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBookingItems",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServiceNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBookingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBookingItems_CustomerBookings_CustomerBookingId",
                        column: x => x.CustomerBookingId,
                        principalSchema: "dbo",
                        principalTable: "CustomerBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPayments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinorAmount = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    StripeIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChargeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClientSecret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProviderPublishableKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IntentLeaseOwnerToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IntentLeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPayments", x => x.Id);
                    table.CheckConstraint("CK_CustomerPayments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_CustomerPayments_Currency", "[Currency] IN ('ILS','USD','EUR')");
                    table.CheckConstraint("CK_CustomerPayments_MinorAmount", "[MinorAmount] > 0");
                    table.ForeignKey(
                        name: "FK_CustomerPayments_CustomerBookings_CustomerBookingId",
                        column: x => x.CustomerBookingId,
                        principalSchema: "dbo",
                        principalTable: "CustomerBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedBookingStatusMessages",
                schema: "dbo",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Applied = table.Column<bool>(type: "bit", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedBookingStatusMessages", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_ProcessedBookingStatusMessages_CustomerBookings_CustomerBookingId",
                        column: x => x.CustomerBookingId,
                        principalSchema: "dbo",
                        principalTable: "CustomerBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogOfferings",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogOfferings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogOfferings_CatalogBranches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "CatalogBranches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CatalogOfferings_CatalogCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "dbo",
                        principalTable: "CatalogCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftSelections",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutDraftItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonChoiceSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftSelections_CheckoutDraftItems_CheckoutDraftItemId",
                        column: x => x.CheckoutDraftItemId,
                        principalSchema: "dbo",
                        principalTable: "CheckoutDraftItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftPricingItemSnapshots",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricingSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftPricingItemSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftPricingItemSnapshots_CheckoutDraftPricingSnapshots_PricingSnapshotId",
                        column: x => x.PricingSnapshotId,
                        principalSchema: "dbo",
                        principalTable: "CheckoutDraftPricingSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBookingSelections",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonChoiceSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddonGroupNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddonChoiceNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddonChoiceNameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SelectionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitDurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalDurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    IsDefaultApplied = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBookingSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBookingSelections_CustomerBookingItems_CustomerBookingItemId",
                        column: x => x.CustomerBookingItemId,
                        principalSchema: "dbo",
                        principalTable: "CustomerBookingItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPaymentIdempotencyRecords",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerDeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPaymentIdempotencyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPaymentIdempotencyRecords_CustomerPayments_CustomerPaymentId",
                        column: x => x.CustomerPaymentId,
                        principalSchema: "dbo",
                        principalTable: "CustomerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StripeWebhookEvents",
                schema: "dbo",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BodyHash = table.Column<string>(type: "char(64)", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DispositionReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChargeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWebhookEvents", x => x.EventId);
                    table.CheckConstraint("CK_StripeWebhookEvents_State", "[State] IN ('Processing','Completed','Quarantined','Deferred')");
                    table.ForeignKey(
                        name: "FK_StripeWebhookEvents_CustomerPayments_CustomerPaymentId",
                        column: x => x.CustomerPaymentId,
                        principalSchema: "dbo",
                        principalTable: "CustomerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CatalogAddonGroups",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAddonGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SelectionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    MinimumSelections = table.Column<int>(type: "int", nullable: false),
                    MaximumSelections = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogAddonGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogAddonGroups_CatalogOfferings_OfferingId",
                        column: x => x.OfferingId,
                        principalSchema: "dbo",
                        principalTable: "CatalogOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutDraftPricingSelectionSnapshots",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricingItemSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonChoiceSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitDurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    TotalDurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    IsDefaultApplied = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutDraftPricingSelectionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutDraftPricingSelectionSnapshots_CheckoutDraftPricingItemSnapshots_PricingItemSnapshotId",
                        column: x => x.PricingItemSnapshotId,
                        principalSchema: "dbo",
                        principalTable: "CheckoutDraftPricingItemSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogAddonChoices",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAddonChoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultQuantity = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogAddonChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogAddonChoices_CatalogAddonGroups_AddonGroupId",
                        column: x => x.AddonGroupId,
                        principalSchema: "dbo",
                        principalTable: "CatalogAddonGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "dbo",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "dbo",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "dbo",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "dbo",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "dbo",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "dbo",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Email",
                schema: "dbo",
                table: "AspNetUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "dbo",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationAttempts_BookingReference",
                schema: "dbo",
                table: "BookingConfirmationAttempts",
                column: "BookingReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationAttempts_OrderGuid",
                schema: "dbo",
                table: "BookingConfirmationAttempts",
                column: "OrderGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingConfirmationAttempts_UserId_OwnerDeviceId",
                schema: "dbo",
                table: "BookingConfirmationAttempts",
                columns: new[] { "UserId", "OwnerDeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonChoices_AddonGroupId_DisplayOrder",
                schema: "dbo",
                table: "CatalogAddonChoices",
                columns: new[] { "AddonGroupId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonChoices_SourceAddonChoiceId",
                schema: "dbo",
                table: "CatalogAddonChoices",
                column: "SourceAddonChoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonGroups_OfferingId_DisplayOrder",
                schema: "dbo",
                table: "CatalogAddonGroups",
                columns: new[] { "OfferingId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogAddonGroups_SourceAddonGroupId",
                schema: "dbo",
                table: "CatalogAddonGroups",
                column: "SourceAddonGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogBranches_ProviderId_DisplayOrder",
                schema: "dbo",
                table: "CatalogBranches",
                columns: new[] { "ProviderId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogBranches_SourceBranchId",
                schema: "dbo",
                table: "CatalogBranches",
                column: "SourceBranchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCategories_ProviderId_DisplayOrder",
                schema: "dbo",
                table: "CatalogCategories",
                columns: new[] { "ProviderId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCategories_SourceCategoryId",
                schema: "dbo",
                table: "CatalogCategories",
                column: "SourceCategoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOfferings_BranchId_DisplayOrder",
                schema: "dbo",
                table: "CatalogOfferings",
                columns: new[] { "BranchId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOfferings_CategoryId_DisplayOrder",
                schema: "dbo",
                table: "CatalogOfferings",
                columns: new[] { "CategoryId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOfferings_SourceOfferingId",
                schema: "dbo",
                table: "CatalogOfferings",
                column: "SourceOfferingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProviders_IsEnabled_DisplayOrder",
                schema: "dbo",
                table: "CatalogProviders",
                columns: new[] { "IsEnabled", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProviders_SourceCompanyId",
                schema: "dbo",
                table: "CatalogProviders",
                column: "SourceCompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftItems_CheckoutDraftId_DisplayOrder",
                schema: "dbo",
                table: "CheckoutDraftItems",
                columns: new[] { "CheckoutDraftId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftItems_CheckoutDraftId_OfferingSourceId",
                schema: "dbo",
                table: "CheckoutDraftItems",
                columns: new[] { "CheckoutDraftId", "OfferingSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingItemSnapshots_PricingSnapshotId_DisplayOrder",
                schema: "dbo",
                table: "CheckoutDraftPricingItemSnapshots",
                columns: new[] { "PricingSnapshotId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingItemSnapshots_PricingSnapshotId_OfferingSourceId",
                schema: "dbo",
                table: "CheckoutDraftPricingItemSnapshots",
                columns: new[] { "PricingSnapshotId", "OfferingSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSelectionSnapshots_PricingItemSnapshotId_AddonChoiceSourceId",
                schema: "dbo",
                table: "CheckoutDraftPricingSelectionSnapshots",
                columns: new[] { "PricingItemSnapshotId", "AddonChoiceSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSelectionSnapshots_PricingItemSnapshotId_DisplayOrder",
                schema: "dbo",
                table: "CheckoutDraftPricingSelectionSnapshots",
                columns: new[] { "PricingItemSnapshotId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSnapshots_CheckoutDraftId",
                schema: "dbo",
                table: "CheckoutDraftPricingSnapshots",
                column: "CheckoutDraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftPricingSnapshots_Currency_CatalogVersion_QuotedAtUtc",
                schema: "dbo",
                table: "CheckoutDraftPricingSnapshots",
                columns: new[] { "Currency", "CatalogVersion", "QuotedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDrafts_OrderGuid",
                schema: "dbo",
                table: "CheckoutDrafts",
                column: "OrderGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDrafts_OwnerDeviceId_ExpiresAt",
                schema: "dbo",
                table: "CheckoutDrafts",
                columns: new[] { "OwnerDeviceId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDrafts_OwnerDeviceId_OrderGuid",
                schema: "dbo",
                table: "CheckoutDrafts",
                columns: new[] { "OwnerDeviceId", "OrderGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftSelections_CheckoutDraftItemId_AddonChoiceSourceId",
                schema: "dbo",
                table: "CheckoutDraftSelections",
                columns: new[] { "CheckoutDraftItemId", "AddonChoiceSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutDraftSelections_CheckoutDraftItemId_DisplayOrder",
                schema: "dbo",
                table: "CheckoutDraftSelections",
                columns: new[] { "CheckoutDraftItemId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingItems_CustomerBookingId_DisplayOrder",
                schema: "dbo",
                table: "CustomerBookingItems",
                columns: new[] { "CustomerBookingId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingItems_CustomerBookingId_OfferingSourceId",
                schema: "dbo",
                table: "CustomerBookingItems",
                columns: new[] { "CustomerBookingId", "OfferingSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_BusinessReservationId",
                schema: "dbo",
                table: "CustomerBookings",
                column: "BusinessReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_BusinessWorkOrderId",
                schema: "dbo",
                table: "CustomerBookings",
                column: "BusinessWorkOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_OrderGuid",
                schema: "dbo",
                table: "CustomerBookings",
                column: "OrderGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_OwnerDeviceId_OrderGuid",
                schema: "dbo",
                table: "CustomerBookings",
                columns: new[] { "OwnerDeviceId", "OrderGuid" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_PublicReference",
                schema: "dbo",
                table: "CustomerBookings",
                column: "PublicReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookings_UserId_CreatedAtUtc",
                schema: "dbo",
                table: "CustomerBookings",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingSelections_CustomerBookingItemId_AddonChoiceSourceId",
                schema: "dbo",
                table: "CustomerBookingSelections",
                columns: new[] { "CustomerBookingItemId", "AddonChoiceSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBookingSelections_CustomerBookingItemId_DisplayOrder",
                schema: "dbo",
                table: "CustomerBookingSelections",
                columns: new[] { "CustomerBookingItemId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerConfigurations_IsActive",
                schema: "dbo",
                table: "CustomerConfigurations",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDevices_InstallationId",
                schema: "dbo",
                table: "CustomerDevices",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDevices_TokenHash",
                schema: "dbo",
                table: "CustomerDevices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalIdempotencyRecords_ExpiresAtUtc",
                schema: "dbo",
                table: "CustomerInternalIdempotencyRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalIdempotencyRecords_OwnerToken",
                schema: "dbo",
                table: "CustomerInternalIdempotencyRecords",
                column: "OwnerToken",
                unique: true,
                filter: "[OwnerToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalIdempotencyRecords_ServiceId_Operation_IdempotencyKey",
                schema: "dbo",
                table: "CustomerInternalIdempotencyRecords",
                columns: new[] { "ServiceId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalIdempotencyRecords_State_LeaseExpiresAtUtc",
                schema: "dbo",
                table: "CustomerInternalIdempotencyRecords",
                columns: new[] { "State", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalServiceNonces_ExpiresAtUtc",
                schema: "dbo",
                table: "CustomerInternalServiceNonces",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInternalServiceNonces_ServiceId_Nonce",
                schema: "dbo",
                table: "CustomerInternalServiceNonces",
                columns: new[] { "ServiceId", "Nonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentIdempotencyRecords_CustomerPaymentId",
                schema: "dbo",
                table: "CustomerPaymentIdempotencyRecords",
                column: "CustomerPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentIdempotencyRecords_UserId_OwnerDeviceId_IdempotencyKey",
                schema: "dbo",
                table: "CustomerPaymentIdempotencyRecords",
                columns: new[] { "UserId", "OwnerDeviceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CustomerBookingId",
                schema: "dbo",
                table: "CustomerPayments",
                column: "CustomerBookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_IntentLeaseExpiresAtUtc",
                schema: "dbo",
                table: "CustomerPayments",
                column: "IntentLeaseExpiresAtUtc",
                filter: "[IntentLeaseOwnerToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_PaymentIntentId",
                schema: "dbo",
                table: "CustomerPayments",
                column: "PaymentIntentId",
                unique: true,
                filter: "[PaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_StripeIdempotencyKey",
                schema: "dbo",
                table: "CustomerPayments",
                column: "StripeIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_UserId_OwnerDeviceId_Id",
                schema: "dbo",
                table: "CustomerPayments",
                columns: new[] { "UserId", "OwnerDeviceId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_UserId_OwnerDeviceId_IdempotencyKey",
                schema: "dbo",
                table: "CustomerPayments",
                columns: new[] { "UserId", "OwnerDeviceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedBookingStatusMessages_CustomerBookingId_Sequence",
                schema: "dbo",
                table: "ProcessedBookingStatusMessages",
                columns: new[] { "CustomerBookingId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_CustomerPaymentId_State_ChargeId",
                schema: "dbo",
                table: "StripeWebhookEvents",
                columns: new[] { "CustomerPaymentId", "State", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_State_CreatedAtUtc",
                schema: "dbo",
                table: "StripeWebhookEvents",
                columns: new[] { "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_UserId",
                schema: "dbo",
                table: "UserAddresses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_UserId_LicensePlate",
                schema: "dbo",
                table: "Vehicles",
                columns: new[] { "UserId", "LicensePlate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BookingConfirmationAttempts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CatalogAddonChoices",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CheckoutDraftPricingSelectionSnapshots",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CheckoutDraftSelections",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerBookingSelections",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerConfigurations",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerDevices",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerInternalIdempotencyRecords",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerInternalServiceNonces",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerPaymentIdempotencyRecords",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ProcessedBookingStatusMessages",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StripeWebhookEvents",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserAddresses",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Vehicles",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CatalogAddonGroups",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CheckoutDraftPricingItemSnapshots",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CheckoutDraftItems",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerBookingItems",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerPayments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CatalogOfferings",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CheckoutDraftPricingSnapshots",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CustomerBookings",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CatalogBranches",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CatalogCategories",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CheckoutDrafts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CatalogProviders",
                schema: "dbo");
        }
    }
}
