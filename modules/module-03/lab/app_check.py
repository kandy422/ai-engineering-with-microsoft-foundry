from pathlib import Path
import subprocess,json,os
root=Path(__file__).resolve().parent.parent
dll=root/"app/bin/Release/net10.0/TicketRouter.dll"
cases=[
    ("broken JSON","{", "invalid"),
    ("missing text","{}", "invalid"),
    ("whitespace",json.dumps({"text":" "}),"invalid"),
    ("unfamiliar",json.dumps({"text":"gyroscope nebula"}),"review"),
    ("ambiguous",json.dumps({"text":"invoice production"}),"review"),
    ("short incident",json.dumps({"text":"checkout down"}),"review"),
    ("strong signal",json.dumps({"text":"Production outage: checkout down, payments failing"}),"routed"),
]
results=[]
for name,request,expected in cases:
    completed=subprocess.run(["dotnet",str(dll)],input=request,text=True,capture_output=True,check=True,timeout=25)
    result=json.loads(completed.stdout)
    assert result["status"]==expected,(name,result)
    results.append({"case":name,"request":request,"result":result})
request=json.dumps({"text":"checkout down"})
environment={**os.environ,"COURSE_PYTHON":str(root/"no-such-python.exe")}
unavailable=subprocess.run(["dotnet",str(dll)],input=request,text=True,capture_output=True,timeout=25,env=environment)
result=json.loads(unavailable.stdout)
assert unavailable.returncode==1 and result=={"status":"unavailable","queue":None}
results.append({"case":"worker could not start","request":request,"result":result})
print(json.dumps(results,indent=2))

