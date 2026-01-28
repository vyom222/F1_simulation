from Python.data_collection import get_curves, get_driver_data
from fastapi import FastAPI
from pydantic import BaseModel
from fastapi.responses import JSONResponse

app = FastAPI()

class TyreRequest(BaseModel):
    country: str
    year: int

@app.post("/tyre_model")
def tyre_model(req: TyreRequest):
    return get_curves(req.country, req.year)

@app.post("/driver_data")
def driver_data(req: TyreRequest):
    return get_driver_data(req.country, req.year)

@app.get("/health")
def health():
    return {"status": "ok"}

@app.get("/hello")
def hello():
    return {"message": "Hello from Python FastAPI!"}


# Run with: uvicorn Python.api:app --reload --port 8000
# The C# application (running on port 5000) will call this API on port 8000
