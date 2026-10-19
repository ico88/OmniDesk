using OmniDesk.Remote.MeshCentral;
if(args.Length is not (3 or 4))throw new ArgumentException("origin username password-file [exercise]");
var client=new MeshCentralClient(new MeshCentralOptions(args[0],args[1],args[2]));
await client.CheckReady(CancellationToken.None);
Console.WriteLine("PASS: real engine TLS and control authentication");
if(args.Length==4&&args[3]=="exercise"){
    MeshAgentProvisioning? group=null;
    try{
        var id=Guid.NewGuid();group=await client.Provision(id,CancellationToken.None);
        var repeated=await client.Provision(id,CancellationToken.None);
        if(repeated.MeshId!=group.MeshId)throw new InvalidOperationException("Provisioning retry duplicated group");
        if(await client.ContainsNode(group.MeshId,"node//"+new string('Q',64),CancellationToken.None))throw new InvalidOperationException("Unregistered node accepted");
        Console.WriteLine("PASS: real engine group policy, idempotent provisioning and rejection of unregistered node");
    }
    finally{if(group is not null)await client.RemoveGroup(group.MeshId,CancellationToken.None);}
    Console.WriteLine("PASS: diagnostic group removed; no desktop or user session tested");
}
