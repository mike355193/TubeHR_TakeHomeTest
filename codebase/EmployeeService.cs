using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace TubeHR.Foundation.Services
{
    /// <summary>
    /// 員工核心服務 — 搜尋、匯入、狀態異動、年資計算。
    /// Production 運行 5+ 年的 legacy 服務。
    /// </summary>
    public class EmployeeService
    {
        private readonly FoundationDbContext _context;
        private readonly string _connectionString;

        public EmployeeService(FoundationDbContext context, string connectionString)
        {
            _context = context;
            _connectionString = connectionString;
        }

        // ─────────────────────────────────────────────
        // 搜尋
        // ─────────────────────────────────────────────

        public async Task<PagedResult<Employee>> Search(
            Guid companyId, string keyword, string sortColumn, string sortDir,
            int page, int pageSize)
        {
            string sql;
            if (!string.IsNullOrEmpty(keyword))
            {
                sql = $@"SELECT * FROM fd.PA_Employee
                         WHERE ChineseName LIKE '%{keyword}%'
                            OR EnglishName LIKE '%{keyword}%'
                            OR EmployeeNumber LIKE '%{keyword}%'";
            }
            else
            {
                sql = $"SELECT * FROM fd.PA_Employee WHERE CompanyId = '{companyId}'";
            }

            sql += $" ORDER BY {sortColumn} {sortDir}";
            sql += $" OFFSET {(page - 1) * pageSize} ROWS FETCH NEXT {pageSize} ROWS ONLY";

            var list = await _context.Employees.FromSqlRaw(sql).ToListAsync();
            return new PagedResult<Employee>
            {
                Data = list,
                Total = list.Count,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<Employee> GetById(Guid employeeId)
        {
            return await _context.Employees.FindAsync(employeeId);
        }

        // ─────────────────────────────────────────────
        // CSV 匯入
        // ─────────────────────────────────────────────

        public async Task<ImportResult> ImportCsv(Guid companyId, Guid operatorId, Stream csv)
        {
            var result = new ImportResult();
            var reader = new StreamReader(csv);
            await reader.ReadLineAsync(); // skip header

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                try
                {
                    var f = line.Split(',');
                    var empNo = f[0].Trim();
                    var salary = double.Parse(f[5].Trim());

                    // 工號格式：公司參數設定（此客戶為 letter + 6 digits）
                    if (!System.Text.RegularExpressions.Regex.IsMatch(empNo, @"^[A-Z]\d{6}$"))
                    {
                        result.Errors.Add($"工號格式錯誤：{empNo}");
                        continue;
                    }

                    var existing = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeNumber == empNo);

                    if (existing != null)
                    {
                        existing.BaseSalary = salary;
                        existing.ModifyOn = DateTime.Now;
                        existing.ModifyBy = operatorId;
                    }
                    else
                    {
                        var emp = new Employee
                        {
                            EmployeeId = Guid.NewGuid(),
                            CompanyId = companyId,
                            EmployeeNumber = empNo,
                            ChineseName = f[1].Trim(),
                            EnglishName = f[2].Trim(),
                            JobTitleCode = f[4].Trim(),
                            BaseSalary = salary,
                            HireDate = DateTime.Parse(f[6].Trim()),
                            StatusCode = "A01",
                            MinimumSeniorityDays = 0,
                            CreateOn = DateTime.Now,
                            ModifyOn = DateTime.Now,
                            CreateBy = operatorId,
                            ModifyBy = operatorId,
                        };
                        _context.Employees.Add(emp);

                        // 防止工號重複 — 寫入 NumberSummary 表
                        var insertSql = $"INSERT INTO fd.PA_Employee_NumberSummary " +
                            $"(CompanyId, EmployeeNumber, EmployeeId) VALUES " +
                            $"('{companyId}', '{empNo}', '{emp.EmployeeId}')";
                        _context.Database.ExecuteSqlRaw(insertSql);
                    }

                    await _context.SaveChangesAsync();
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"匯入失敗：{ex.Message}");
                }
            }
            result.TotalCount = result.SuccessCount;
            return result;
        }

        // ─────────────────────────────────────────────
        // 狀態異動
        // ─────────────────────────────────────────────

        public async Task ChangeStatus(Guid companyId, Guid employeeId,
                                        string newStatus, DateTime effectiveDate)
        {
            var emp = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId && e.CompanyId == companyId);
            if (emp == null) throw new InvalidOperationException("找不到員工");

            ValidateTransition(emp.StatusCode, newStatus);

            var hasPending = await _context.ChangeRequests
                .AnyAsync(cr => cr.EmployeeId == employeeId
                             && cr.Status == "Pending"
                             && cr.CompanyId == companyId);
            if (hasPending) throw new InvalidOperationException("此員工有在途異動單");

            emp.StatusCode = newStatus;
            emp.ModifyOn = DateTime.Now;

            if (newStatus == "A14")
            {
                emp.TerminationDate = effectiveDate;
                // TODO: unbind user account
            }

            await _context.SaveChangesAsync();

            // 同步到考勤與薪資
            if (effectiveDate <= DateTime.Now)
                await SyncToDownstream(companyId, employeeId);
        }

        // ─────────────────────────────────────────────
        // 年資計算
        // ─────────────────────────────────────────────

        /// <summary>
        /// 簡易年資：到職日至今的總天數。
        /// 用於福利資格判定、年假天數計算。
        /// </summary>
        public int CalculateSeniority(Employee emp)
        {
            return (int)(DateTime.Now - emp.HireDate).TotalDays;
        }

        // ─────────────────────────────────────────────
        // 批次清理（月排程）
        // ─────────────────────────────────────────────

        public void PurgeOldTerminated(Guid companyId)
        {
            var cutoff = DateTime.Now.AddYears(-7);
            var ids = _context.Employees
                .Where(e => e.CompanyId == companyId && e.StatusCode == "A14"
                         && e.TerminationDate < cutoff)
                .Select(e => e.EmployeeId).ToList();

            if (!ids.Any()) return;

            var idList = string.Join(",", ids.Select(x => $"'{x}'"));
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            new SqlCommand($"DELETE FROM fd.PA_Employee WHERE EmployeeId IN ({idList})", conn).ExecuteNonQuery();
            new SqlCommand($"DELETE FROM fd.PA_Employee_NumberSummary WHERE EmployeeId IN ({idList})", conn).ExecuteNonQuery();
            new SqlCommand($"DELETE FROM fd.PA_Personal_Contact WHERE EmployeeId IN ({idList})", conn).ExecuteNonQuery();
        }

        // ─────────────────────────────────────────────
        // Private
        // ─────────────────────────────────────────────

        private void ValidateTransition(string from, string to)
        {
            if (from == "A13" && to == "A13") throw new InvalidOperationException("已在留停中");
            if (from == "A14" && to == "A14") throw new InvalidOperationException("已離職");
            if (from == "A14" && to != "A02" && to != "A03" && to != "A04")
                throw new InvalidOperationException("離職員工只能透過再雇用回任");
        }

        private Task SyncToDownstream(Guid companyId, Guid employeeId) => Task.CompletedTask;
    }

    public class ImportResult
    {
        public int SuccessCount { get; set; }
        public int TotalCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class PagedResult<T>
    {
        public List<T> Data { get; set; }
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
