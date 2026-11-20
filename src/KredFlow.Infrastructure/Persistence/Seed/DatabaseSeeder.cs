using Bogus;
using KredFlow.Application.Interfaces;
using KredFlow.Domain.Entities;
using KredFlow.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KredFlow.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds 2 lenders (each with an owner login), 10 customers and 15 loans with schedules, payments and transactions.
/// Output is deterministic for a given date. Development only: the owner password below is public.
/// </summary>
public class DatabaseSeeder(
    KredFlowDbContext db,
    TimeProvider? timeProvider = null,
    IPasswordHasher<ApplicationUser>? passwordHasher = null) : IDatabaseSeeder
{
    public const string DevOwnerPassword = "KredFlow@Dev2026!";

    private const int RandomSeed = 20260101;
    private const int CustomerCount = 10;
    private const int LoanCount = 15;
    private const decimal PenaltyRate = 0.02m;

    private static readonly decimal[] Principals = [25_000m, 50_000m, 100_000m, 200_000m, 500_000m];
    private static readonly int[] Tenures = [6, 12, 18, 24, 36];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Lenders.AnyAsync(cancellationToken))
        {
            return;
        }

        var today = DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        var faker = new Faker("en_IND") { Random = new Randomizer(RandomSeed) };

        var lenders = CreateLenders(faker, today);
        var customers = CreateCustomers(faker, lenders, today);

        var loans = new List<Loan>();
        var schedules = new List<RepaymentSchedule>();
        var payments = new List<Payment>();
        var allocations = new List<PaymentAllocation>();
        var transactions = new List<LoanTransaction>();

        for (var i = 0; i < LoanCount; i++)
        {
            var customer = customers[i % customers.Count];
            BuildLoan(faker, i, customer, today, loans, schedules, payments, allocations, transactions);
        }

        db.Lenders.AddRange(lenders);
        AddOwnerUsers(lenders);
        db.Customers.AddRange(customers);
        db.Loans.AddRange(loans);
        db.RepaymentSchedules.AddRange(schedules);
        db.Payments.AddRange(payments);
        db.PaymentAllocations.AddRange(allocations);
        db.LoanTransactions.AddRange(transactions);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<Lender> CreateLenders(Faker faker, DateOnly today)
    {
        var createdAt = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return
        [
            new()
            {
                Id = faker.Random.Guid(),
                BusinessName = "Annapoorna Finance",
                OwnerName = "Annapoorna Devi",
                Email = "contact@annapoorna-finance.example",
                PhoneNumber = "+919876500001",
                Address = "Chennai",
                CreatedAt = createdAt
            },
            new()
            {
                Id = faker.Random.Guid(),
                BusinessName = "Lakshmi Micro Credit",
                OwnerName = "Lakshmi Narayanan",
                Email = "hello@lakshmi-microcredit.example",
                PhoneNumber = "+919876500002",
                Address = "Bangalore",
                CreatedAt = createdAt
            }
        ];
    }

    private void AddOwnerUsers(List<Lender> lenders)
    {
        var hasher = passwordHasher ?? new PasswordHasher<ApplicationUser>();
        foreach (var lender in lenders)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                LenderId = lender.Id,
                UserName = lender.Email,
                NormalizedUserName = lender.Email.ToUpperInvariant(),
                Email = lender.Email,
                NormalizedEmail = lender.Email.ToUpperInvariant(),
                EmailConfirmed = true,
                PhoneNumber = lender.PhoneNumber,
                FullName = lender.OwnerName,
                IsActive = true,
                LockoutEnabled = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                CreatedAt = lender.CreatedAt
            };
            user.PasswordHash = hasher.HashPassword(user, DevOwnerPassword);

            db.Users.Add(user);
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = KredFlowDbContext.OwnerRoleId });
        }
    }

    private static List<Customer> CreateCustomers(Faker faker, List<Lender> lenders, DateOnly today)
    {
        var createdAt = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var customers = new List<Customer>();
        for (var i = 0; i < CustomerCount; i++)
        {
            customers.Add(new Customer
            {
                Id = faker.Random.Guid(),
                LenderId = lenders[i % lenders.Count].Id,
                FullName = faker.Name.FullName(),
                PhoneNumber = $"+91{faker.Random.Int(6, 9)}{faker.Random.Int(0, 999_999_999):D9}",
                CreatedAt = createdAt
            });
        }

        return customers;
    }

    private static void BuildLoan(
        Faker faker,
        int index,
        Customer customer,
        DateOnly today,
        List<Loan> loans,
        List<RepaymentSchedule> schedules,
        List<Payment> payments,
        List<PaymentAllocation> allocations,
        List<LoanTransaction> transactions)
    {
        // Scenario cycles: 0 = closed, 1-2 = active on time, 3 = overdue, 4 = defaulted.
        var scenario = index % 5;
        var tenure = faker.Random.ArrayElement(Tenures);
        var principal = faker.Random.ArrayElement(Principals);
        var rate = Math.Round(faker.Random.Decimal(10m, 24m), 1);

        var monthsElapsed = scenario switch
        {
            0 => tenure + 1,
            3 => faker.Random.Int(3, tenure - 1),
            4 => faker.Random.Int(5, tenure - 1),
            _ => faker.Random.Int(1, tenure - 1)
        };
        var missedInstallments = scenario switch { 3 => 2, 4 => 3, _ => 0 };

        var startDate = today.AddMonths(-monthsElapsed);
        var emi = CalculateEmi(principal, rate, tenure);

        var loan = new Loan
        {
            Id = faker.Random.Guid(),
            LenderId = customer.LenderId,
            CustomerId = customer.Id,
            Principal = principal,
            AnnualInterestRate = rate,
            TenureMonths = tenure,
            EmiAmount = emi,
            StartDate = startDate,
            CreatedAt = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        };

        var loanSchedules = BuildSchedule(faker, loan);
        var dueSchedules = loanSchedules.Where(s => s.DueDate <= today).ToList();
        var paidCount = dueSchedules.Count - missedInstallments;

        loan.Status = missedInstallments switch
        {
            0 when paidCount == tenure => LoanStatus.Closed,
            0 => LoanStatus.Active,
            >= 3 => LoanStatus.Defaulted,
            _ => LoanStatus.Overdue
        };

        transactions.Add(new LoanTransaction
        {
            Id = faker.Random.Guid(),
            LoanId = loan.Id,
            TransactionType = TransactionType.Disbursement,
            Amount = principal,
            TransactionDate = startDate
        });

        DateOnly? lastPaymentDate = null;
        for (var n = 0; n < dueSchedules.Count; n++)
        {
            var schedule = dueSchedules[n];

            if (n < paidCount)
            {
                var paymentDate = Min(schedule.DueDate.AddDays(faker.Random.Int(0, 3)), today);
                lastPaymentDate = paymentDate;
                var payment = new Payment
                {
                    Id = faker.Random.Guid(),
                    LoanId = loan.Id,
                    Amount = schedule.TotalDue,
                    PaymentDate = paymentDate,
                    PaymentMethod = faker.Random.Enum<PaymentMethod>(),
                    CreatedAt = paymentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                };
                payments.Add(payment);

                allocations.Add(new PaymentAllocation
                {
                    Id = faker.Random.Guid(),
                    PaymentId = payment.Id,
                    ScheduleId = schedule.Id,
                    InterestAmount = schedule.InterestDue,
                    PrincipalAmount = schedule.PrincipalDue
                });

                transactions.Add(new LoanTransaction
                {
                    Id = faker.Random.Guid(),
                    LoanId = loan.Id,
                    TransactionType = TransactionType.Repayment,
                    Amount = payment.Amount,
                    TransactionDate = paymentDate
                });
            }
            else
            {
                transactions.Add(new LoanTransaction
                {
                    Id = faker.Random.Guid(),
                    LoanId = loan.Id,
                    TransactionType = TransactionType.Penalty,
                    Amount = Math.Round(schedule.TotalDue * PenaltyRate, 2),
                    TransactionDate = Min(schedule.DueDate.AddDays(7), today)
                });
            }
        }

        if (loan.Status == LoanStatus.Closed)
        {
            loan.ClosedDate = lastPaymentDate;
        }

        loans.Add(loan);
        schedules.AddRange(loanSchedules);
    }

    private static List<RepaymentSchedule> BuildSchedule(Faker faker, Loan loan)
    {
        var monthlyRate = loan.AnnualInterestRate / 1200m;
        var balance = loan.Principal;
        var result = new List<RepaymentSchedule>(loan.TenureMonths);

        for (var n = 1; n <= loan.TenureMonths; n++)
        {
            var interest = Math.Round(balance * monthlyRate, 2);
            var principalDue = n == loan.TenureMonths ? balance : Math.Round(loan.EmiAmount - interest, 2);
            var openingPrincipal = balance;
            balance -= principalDue;

            result.Add(new RepaymentSchedule
            {
                Id = faker.Random.Guid(),
                LoanId = loan.Id,
                InstallmentNumber = n,
                DueDate = loan.StartDate.AddMonths(n),
                OpeningPrincipal = openingPrincipal,
                PrincipalDue = principalDue,
                InterestDue = interest,
                TotalDue = principalDue + interest
            });
        }

        return result;
    }

    private static decimal CalculateEmi(decimal principal, decimal annualRatePercent, int months)
    {
        var r = (double)annualRatePercent / 1200d;
        var factor = Math.Pow(1 + r, months);
        return Math.Round((decimal)((double)principal * r * factor / (factor - 1)), 2);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;
}
