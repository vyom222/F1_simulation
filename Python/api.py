from Python.data_collection import get_curves, get_driver_data, get_sessions
from fastapi import FastAPI
from pydantic import BaseModel
from fastapi.responses import JSONResponse

app = FastAPI()

class TyreRequest(BaseModel):
    session_keys: list[int]

class SessionRequest(BaseModel):
    circuit: str
    year: int

# GET SESSIONS AND MAKE A FUCNTION FOR SESSIONS
@app.post("/tyre_model")
def tyre_model(req: TyreRequest):
    try:
        return get_curves(req.session_keys)
    except Exception as e:
        return JSONResponse(status_code=500, content={"error": str(e)})

@app.post("/session_keys")
def session_keys(req: SessionRequest):
    try:
        keys = get_sessions(req.circuit, req.year)
        return keys
    except Exception as e:
        return JSONResponse(status_code=500, content={"error": str(e)})


@app.post("/driver_data")
def driver_data(req: TyreRequest):
    try:
        return get_driver_data(req.session_keys)
    except Exception as e:
        return JSONResponse(status_code=500, content={"error": str(e)})

@app.get("/health")
def health():
    return {"status": "ok"}

@app.get("/hello")
def hello():
    return {"message": "Hello from Python FastAPI!"}


# Run with: uvicorn Python.api:app --reload --port 8000
# The C# application (running on port 5000) will call this API on port 8000
