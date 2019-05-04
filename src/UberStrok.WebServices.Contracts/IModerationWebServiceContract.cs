﻿using System.ServiceModel;

namespace UberStrok.WebServices.Contracts
{
    [ServiceContract]
    public interface IModerationWebServiceContract
    {
        [OperationContract]
        byte[] BanCmid(byte[] data);

        [OperationContract]
        byte[] BanIp(byte[] data);

        [OperationContract]
        byte[] BanHwd(byte[] data);
    }
}
