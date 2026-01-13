from Python.data_collection import get_curves, get_driver_data
from fastapi import FastAPI
from pydantic import BaseModel

# out = get_curves("Spain", 2024)
# print(out)

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

# Return that it is working
@app.get("/health")
def health():
    return {"status": "ok"}


# uvicorn Python.api:app --reload
