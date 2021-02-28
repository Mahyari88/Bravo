﻿using Dax.Metadata;
using Dax.ViewModel;
using Microsoft.AnalysisServices;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Logging;
using Sqlbi.Bravo.Core.Logging;
using Sqlbi.Bravo.Core.Services.Interfaces;
using Sqlbi.Bravo.Core.Settings;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sqlbi.Bravo.Core.Services
{
    internal class AnalyzeModelService : IAnalyzeModelService, IDisposable
    {
        private readonly SemaphoreSlim _initilizeOrRefreshSemaphore = new SemaphoreSlim(1);
        private readonly ILogger _logger;
        private readonly Server _server;
        private Model _daxModel;
        private bool _disposed;
        private VpaModel _vpaModel;

        public AnalyzeModelService(ILogger<AnalyzeModelService> logger)
        {
            _logger = logger;

            _logger.Trace();
            _server = new Server();
        }

        public async Task InitilizeOrRefreshAsync(RuntimeSummary runtimeSummary)
        {
            _logger.Trace();

            await _initilizeOrRefreshSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(InitilizeOrRefresh).ConfigureAwait(false);
            }
            finally
            {
                _initilizeOrRefreshSemaphore.Release();
            }

            void InitilizeOrRefresh()
            {
                if (runtimeSummary.UsingLocalModelForAnanlysis)
                {
                    return;
                }

                if (_server.Connected == false)
                {
                    _server.Connect(runtimeSummary?.ServerName);
                }

                var db = _server.Databases[runtimeSummary.DatabaseName];
                var tomModel = db.Model;
                _daxModel = Dax.Metadata.Extractor.TomExtractor.GetDaxModel(tomModel, AppConstants.ApplicationName, AppConstants.ApplicationProductVersion);

                var connString = GetConnectionString(runtimeSummary.ServerName, runtimeSummary.DatabaseName);

                using var connection = new AdomdConnection(connString);
                // Populate statistics from DMV
                Dax.Metadata.Extractor.DmvExtractor.PopulateFromDmv(_daxModel, connection, runtimeSummary.ServerName, runtimeSummary.DatabaseName, AppConstants.ApplicationName, AppConstants.ApplicationProductVersion);
                // Populate statistics by querying the data model
                Dax.Metadata.Extractor.StatExtractor.UpdateStatisticsModel(_daxModel, connection, 10);

                _vpaModel = new VpaModel(_daxModel);
            }
        }

        public DateTime GetLastSyncTime()
            => _vpaModel?.Model?.ExtractionDate ?? DateTime.MinValue;

        public (long DatasetSize, int ColumnCount) GetDatasetSummary()
            => (_vpaModel.Tables.Sum(t => t.ColumnsTotalSize), _vpaModel.Columns.Count());

        public List<VpaColumn> GetUnusedColumns() =>
            _vpaModel.Columns.Where(c => !c.IsReferenced).ToList();

        public IEnumerable<VpaColumn> GetAllColumns() => _vpaModel?.Columns;

        public IEnumerable<VpaTable> GetAllTables() => _vpaModel?.Tables;

        private static string GetConnectionString(string dataSourceOrConnectionString, string databaseName)
        {
            var csb = new OleDbConnectionStringBuilder();
            try
            {
                csb.ConnectionString = dataSourceOrConnectionString;
            }
            catch
            {
                // Assume servername
                csb.Provider = "MSOLAP";
                csb.DataSource = dataSourceOrConnectionString;
            }
            csb["Initial Catalog"] = databaseName;
            return csb.ConnectionString;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_server.Connected)
                        _server.Disconnect(endSession: true);

                    _server.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public Model GetModelForExport() => _daxModel;

        public void OverrideDaxModel(Model daxModel)
        {
            _daxModel = daxModel;
            _vpaModel = new VpaModel(_daxModel);
        }
    }
}
