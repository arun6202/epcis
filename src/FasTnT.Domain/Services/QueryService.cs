﻿using FasTnT.Domain.BackgroundTasks;
using FasTnT.Domain.Extensions;
using FasTnT.Domain.Persistence;
using FasTnT.Model.Exceptions;
using FasTnT.Model.Queries;
using FasTnT.Model.Queries.Implementations;
using FasTnT.Model.Responses;
using FasTnT.Model.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FasTnT.Domain.Services
{
    public class QueryService
    {
        private readonly IEpcisQuery[] _queries;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISubscriptionBackgroundService _backgroundService;

        public QueryService(IEpcisQuery[] queries, IUnitOfWork unitOfWork, ISubscriptionBackgroundService backgroundService)
        {
            _queries = queries;
            _unitOfWork = unitOfWork;
            _backgroundService = backgroundService;
        }

        public Task<GetQueryNamesResponse> GetQueryNames(CancellationToken cancellationToken) => Task.Run(() => new GetQueryNamesResponse { QueryNames = _queries.Select(x => x.Name) }, cancellationToken);
        public Task<GetStandardVersionResponse> GetStandardVersion(CancellationToken cancellationToken) => Task.Run(() => new GetStandardVersionResponse { Version = Constants.StandardVersion }, cancellationToken);
        public Task<GetVendorVersionResponse> GetVendorVersion(CancellationToken cancellationToken) => Task.Run(() => new GetVendorVersionResponse { Version = Constants.ProductVersion }, cancellationToken);
        public async Task<GetSubscriptionIdsResult> GetSubscriptionId(GetSubscriptionIds query, CancellationToken cancellationToken)
        {
            var subscriptions = await _unitOfWork.SubscriptionManager.GetAll(false, cancellationToken: cancellationToken);
            return new GetSubscriptionIdsResult { SubscriptionIds = subscriptions.Where(s => s.QueryName == query.QueryName).Select(x => x.SubscriptionId) };
        }

        public async Task<PollResponse> Poll(Poll query, CancellationToken cancellationToken)
        {
            var epcisQuery = _queries.SingleOrDefault(x => x.Name == query.QueryName);

            if (epcisQuery == null)
            {
                throw new Exception($"Unknown query: '{query.QueryName}'");
            }
            else
            {
                query.Parameters = QueryParameterFormatter.Format(query.Parameters);
                try
                {
                    epcisQuery.ValidateParameters(query.Parameters);

                    var results = await epcisQuery.Execute(query.Parameters, _unitOfWork, cancellationToken);
                    return new PollResponse { QueryName = query.QueryName, Entities = results };
                }
                catch (Exception ex) when (!(ex is EpcisException))
                {
                    throw new EpcisException(ExceptionType.QueryParameterException, ex.Message);
                }
            }
        }

        public async Task Subscribe(Subscription request, CancellationToken cancellationToken)
        {
            EnsureDestinationIsValidURI(request);
            EnsureQueryAllowsSubscription(request);
            EnsureDestinationHasEndSlash(request);
            request.Parameters = QueryParameterFormatter.Format(request.Parameters);

            await _unitOfWork.Execute(async tx =>
            {
                await EnsureSubscriptionDoesNotExist(tx, request, cancellationToken);
                await tx.SubscriptionManager.Store(request, cancellationToken);
                _backgroundService.Register(request);
            });
        }

        public async Task Unsubscribe(UnsubscribeRequest query, CancellationToken cancellationToken)
        {
            await _unitOfWork.Execute(async tx =>
            {
                var subscription = await tx.SubscriptionManager.GetById(query.SubscriptionId, cancellationToken);
                if (subscription == null) throw new EpcisException(ExceptionType.NoSuchNameException, $"Subscription with ID '{query.SubscriptionId}' does not exist.");

                await tx.SubscriptionManager.Delete(subscription.SubscriptionId, cancellationToken);
                _backgroundService.Remove(subscription);
            });
        }

        private void EnsureDestinationHasEndSlash(Subscription request) => request.Destination = $"{request.Destination.TrimEnd('/')}/";
        private void EnsureDestinationIsValidURI(Subscription request) => UriValidator.Validate(request.Destination, true);

        private async static Task EnsureSubscriptionDoesNotExist(IUnitOfWork transaction, Subscription request, CancellationToken cancellationToken)
        {
            var subscription = await transaction.SubscriptionManager.GetById(request.SubscriptionId, cancellationToken);
            if (subscription != null) throw new EpcisException(ExceptionType.SubscribeNotPermittedException, $"Subscription '{request.SubscriptionId}' already exist.");
        }

        private void EnsureQueryAllowsSubscription(Subscription subscribe)
        {
            var query = _queries.SingleOrDefault(x => x.Name == subscribe.QueryName);
            if (query == null || !query.AllowSubscription) throw new EpcisException(ExceptionType.SubscribeNotPermittedException, $"Query '{subscribe.QueryName}' does not exist or doesn't allow subscription");
        }
    }

    public class QueryParameterFormatter
    {
        public static IEnumerable<QueryParameter> Format(IEnumerable<QueryParameter> parameters)
            => parameters.GroupBy(x => x.Name)
                         .Select(g => new QueryParameter
                         {
                             Name = g.Key,
                             Values = g.SelectMany(x => x.Values).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                         })
                         .Where(x => x.Values.Any());
    }
}
