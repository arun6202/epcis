﻿using FasTnT.Domain;
using FasTnT.Host.Infrastructure.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FasTnT.Host.Controllers.v1_2
{
    [Authorize]
    [Formatter(Format.Soap)]
    [Route("v1_2/Query.svc")]
    public class EpcisSoapQueryService : EpcisQueryController
    {
        public EpcisSoapQueryService(QueryDispatcher dispatcher) : base(dispatcher)
        {
        }
    }
}
