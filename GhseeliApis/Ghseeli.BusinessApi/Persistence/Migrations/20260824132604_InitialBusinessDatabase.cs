using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghseeli.BusinessApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AppointmentReservations",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerBookingReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    RequestedSlotStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedSlotEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StatusSequence = table.Column<long>(type: "bigint", nullable: false),
                    StatusChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReservations", x => x.Id);
                });

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
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
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
                name: "Companies",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServiceAreaDescriptionAr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServiceAreaDescriptionHe = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InternalServiceIdempotencyRecords",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalServiceIdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InternalServiceNonces",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalServiceNonces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookingStatusOutboxMessages",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkOrderPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeliveryState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeliveryGeneration = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequeuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequeuedByAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequeueRequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStatusOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_BookingStatusOutboxMessages_DeliveryGeneration", "[DeliveryGeneration] >= 0");
                    table.CheckConstraint("CK_BookingStatusOutboxMessages_RequeueAudit", "([RequeuedAtUtc] IS NULL AND [RequeuedByAdminUserId] IS NULL AND [RequeueRequestId] IS NULL) OR ([RequeuedAtUtc] IS NOT NULL AND [RequeuedByAdminUserId] IS NOT NULL AND [RequeueRequestId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BookingStatusOutboxMessages_AppointmentReservations_AppointmentReservationId",
                        column: x => x.AppointmentReservationId,
                        principalSchema: "dbo",
                        principalTable: "AppointmentReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    CustomerPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
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
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrders_AppointmentReservations_AppointmentReservationId",
                        column: x => x.AppointmentReservationId,
                        principalSchema: "dbo",
                        principalTable: "AppointmentReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "Branches",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AddressHe = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceCategories",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceCategories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingStatusRequeueHistory",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingStatusOutboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequeuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStatusRequeueHistory", x => x.Id);
                    table.CheckConstraint("CK_BookingStatusRequeueHistory_Generation", "[Generation] > 0");
                    table.ForeignKey(
                        name: "FK_BookingStatusRequeueHistory_BookingStatusOutboxMessages_BookingStatusOutboxMessageId",
                        column: x => x.BookingStatusOutboxMessageId,
                        principalSchema: "dbo",
                        principalTable: "BookingStatusOutboxMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderItems",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    BaseSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AddonSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ItemSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDurationMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderItems_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "dbo",
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchAvailabilityOverrides",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverrideDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    StartLocalTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndLocalTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    SlotDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchAvailabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchAvailabilityOverrides_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchAvailabilitySettings",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MinimumLeadMinutes = table.Column<int>(type: "int", nullable: false),
                    BookingHorizonDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchAvailabilitySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchAvailabilitySettings_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchRecurringSchedules",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartLocalTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndLocalTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    SlotDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchRecurringSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchRecurringSchedules_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchServiceAreas",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CenterLatitude = table.Column<double>(type: "float", nullable: true),
                    CenterLongitude = table.Column<double>(type: "float", nullable: true),
                    RadiusKm = table.Column<double>(type: "float", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchServiceAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchServiceAreas_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessUserAssignments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Role = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessUserAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessUserAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessUserAssignments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BusinessUserAssignments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOfferings",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOfferings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceOfferings_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "dbo",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceOfferings_ServiceCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "dbo",
                        principalTable: "ServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderSelections",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonChoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_WorkOrderSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderSelections_WorkOrderItems_WorkOrderItemId",
                        column: x => x.WorkOrderItemId,
                        principalSchema: "dbo",
                        principalTable: "WorkOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AddonGroups",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SelectionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    MinimumSelections = table.Column<int>(type: "int", nullable: false),
                    MaximumSelections = table.Column<int>(type: "int", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddonGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddonGroups_ServiceOfferings_ServiceOfferingId",
                        column: x => x.ServiceOfferingId,
                        principalSchema: "dbo",
                        principalTable: "ServiceOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AddonChoices",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddonGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameHe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionHe = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PriceAdjustment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DurationAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultQuantity = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddonChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AddonChoices_AddonGroups_AddonGroupId",
                        column: x => x.AddonGroupId,
                        principalSchema: "dbo",
                        principalTable: "AddonGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddonChoices_AddonGroupId_DisplayOrder",
                schema: "dbo",
                table: "AddonChoices",
                columns: new[] { "AddonGroupId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AddonChoices_AddonGroupId_IsActive",
                schema: "dbo",
                table: "AddonChoices",
                columns: new[] { "AddonGroupId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AddonGroups_ServiceOfferingId_DisplayOrder",
                schema: "dbo",
                table: "AddonGroups",
                columns: new[] { "ServiceOfferingId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AddonGroups_ServiceOfferingId_IsActive",
                schema: "dbo",
                table: "AddonGroups",
                columns: new[] { "ServiceOfferingId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_BranchId_RequestedSlotStartUtc_RequestedSlotEndUtc_Status",
                schema: "dbo",
                table: "AppointmentReservations",
                columns: new[] { "BranchId", "RequestedSlotStartUtc", "RequestedSlotEndUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_CustomerBookingReference",
                schema: "dbo",
                table: "AppointmentReservations",
                column: "CustomerBookingReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_OrderGuid",
                schema: "dbo",
                table: "AppointmentReservations",
                column: "OrderGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReservations_PublicId",
                schema: "dbo",
                table: "AppointmentReservations",
                column: "PublicId",
                unique: true);

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
                name: "UserNameIndex",
                schema: "dbo",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusOutboxMessages_AppointmentReservationId_Sequence",
                schema: "dbo",
                table: "BookingStatusOutboxMessages",
                columns: new[] { "AppointmentReservationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusOutboxMessages_DeliveryState_NextAttemptAtUtc_LeaseExpiresAtUtc",
                schema: "dbo",
                table: "BookingStatusOutboxMessages",
                columns: new[] { "DeliveryState", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusRequeueHistory_BookingStatusOutboxMessageId_Generation",
                schema: "dbo",
                table: "BookingStatusRequeueHistory",
                columns: new[] { "BookingStatusOutboxMessageId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingStatusRequeueHistory_BookingStatusOutboxMessageId_RequestId",
                schema: "dbo",
                table: "BookingStatusRequeueHistory",
                columns: new[] { "BookingStatusOutboxMessageId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchAvailabilityOverrides_BranchId_OverrideDate",
                schema: "dbo",
                table: "BranchAvailabilityOverrides",
                columns: new[] { "BranchId", "OverrideDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchAvailabilitySettings_BranchId",
                schema: "dbo",
                table: "BranchAvailabilitySettings",
                column: "BranchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_CompanyId_IsActive",
                schema: "dbo",
                table: "Branches",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchRecurringSchedules_BranchId_DayOfWeek_IsActive",
                schema: "dbo",
                table: "BranchRecurringSchedules",
                columns: new[] { "BranchId", "DayOfWeek", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchServiceAreas_BranchId",
                schema: "dbo",
                table: "BranchServiceAreas",
                column: "BranchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessUserAssignments_BranchId",
                schema: "dbo",
                table: "BusinessUserAssignments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessUserAssignments_CompanyId",
                schema: "dbo",
                table: "BusinessUserAssignments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessUserAssignments_UserId_CompanyId_BranchId",
                schema: "dbo",
                table: "BusinessUserAssignments",
                columns: new[] { "UserId", "CompanyId", "BranchId" },
                unique: true,
                filter: "[BranchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InternalServiceIdempotencyRecords_ExpiresAtUtc",
                schema: "dbo",
                table: "InternalServiceIdempotencyRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InternalServiceIdempotencyRecords_ServiceId_Operation_IdempotencyKey",
                schema: "dbo",
                table: "InternalServiceIdempotencyRecords",
                columns: new[] { "ServiceId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternalServiceNonces_ExpiresAtUtc",
                schema: "dbo",
                table: "InternalServiceNonces",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InternalServiceNonces_ServiceId_Nonce",
                schema: "dbo",
                table: "InternalServiceNonces",
                columns: new[] { "ServiceId", "Nonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCategories_CompanyId_DisplayOrder",
                schema: "dbo",
                table: "ServiceCategories",
                columns: new[] { "CompanyId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCategories_CompanyId_IsActive",
                schema: "dbo",
                table: "ServiceCategories",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOfferings_BranchId_IsActive",
                schema: "dbo",
                table: "ServiceOfferings",
                columns: new[] { "BranchId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOfferings_CategoryId_DisplayOrder",
                schema: "dbo",
                table: "ServiceOfferings",
                columns: new[] { "CategoryId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOfferings_CategoryId_IsActive",
                schema: "dbo",
                table: "ServiceOfferings",
                columns: new[] { "CategoryId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderItems_WorkOrderId_DisplayOrder",
                schema: "dbo",
                table: "WorkOrderItems",
                columns: new[] { "WorkOrderId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderItems_WorkOrderId_OfferingId",
                schema: "dbo",
                table: "WorkOrderItems",
                columns: new[] { "WorkOrderId", "OfferingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AppointmentReservationId",
                schema: "dbo",
                table: "WorkOrders",
                column: "AppointmentReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_PublicId",
                schema: "dbo",
                table: "WorkOrders",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderSelections_WorkOrderItemId_AddonChoiceId",
                schema: "dbo",
                table: "WorkOrderSelections",
                columns: new[] { "WorkOrderItemId", "AddonChoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderSelections_WorkOrderItemId_DisplayOrder",
                schema: "dbo",
                table: "WorkOrderSelections",
                columns: new[] { "WorkOrderItemId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddonChoices",
                schema: "dbo");

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
                name: "BookingStatusRequeueHistory",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BranchAvailabilityOverrides",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BranchAvailabilitySettings",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BranchRecurringSchedules",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BranchServiceAreas",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BusinessUserAssignments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "InternalServiceIdempotencyRecords",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "InternalServiceNonces",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "WorkOrderSelections",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AddonGroups",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BookingStatusOutboxMessages",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "WorkOrderItems",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ServiceOfferings",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "WorkOrders",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Branches",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ServiceCategories",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AppointmentReservations",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "dbo");
        }
    }
}
