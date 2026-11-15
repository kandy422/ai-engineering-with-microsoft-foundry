import json
import sys
from pathlib import Path
from collections import Counter
import joblib
import numpy as np
import sklearn
from sklearn.cluster import KMeans
from sklearn.feature_extraction.text import CountVectorizer, TfidfVectorizer
from sklearn.linear_model import LinearRegression, LogisticRegression
from sklearn.metrics import confusion_matrix, mean_absolute_error, root_mean_squared_error
from sklearn.model_selection import StratifiedKFold, validation_curve
from sklearn.pipeline import make_pipeline
from sklearn.tree import DecisionTreeClassifier
from baseline import rows, report

ROOT = Path(__file__).resolve().parent
MODEL = ROOT / "router.joblib"
def xy(split):
    return tuple(map(list, zip(*rows(split))))

def fit():
    model = make_pipeline(CountVectorizer(), LogisticRegression(max_iter=1000))
    model.fit(*xy("train"))
    joblib.dump(model, MODEL)
    model = joblib.load(MODEL)
    result = report(lambda text: str(model.predict([text])[0]), rows("development"))
    v = model.named_steps["countvectorizer"]
    lr = model.named_steps["logisticregression"]
    result["featureExamples"] = {word: round(float(lr.coef_[0,v.vocabulary_[word]]),3)
                                 for word in ("down", "invoice", "production")}
    return result

def metrics():
    model = joblib.load(MODEL)
    text, actual = xy("development")
    predicted = model.predict(text)
    tn, fp, fn, tp = map(int, confusion_matrix(actual,predicted,labels=["routine","urgent"]).ravel())
    return {"tn":tn,"fp":fp,"fn":fn,"tp":tp,
            "precision":tp/(tp+fp) if tp+fp else None,
            "recall":tp/(tp+fn) if tp+fn else None,
            "f1":2*tp/(2*tp+fp+fn),
            "mistakes":[[t,a,str(p)] for t,a,p in zip(text,actual,predicted) if a!=p]}

def complexity():
    # Controlled sandbox: incident signal + noise, with a noisy training label.
    rng = np.random.default_rng(19)
    x = rng.normal(size=(160, 12))
    y = (x[:,0] + .65*x[:,1] > 0).astype(int)
    y[rng.choice(len(y), 19, replace=False)] ^= 1
    depths = [1,2,3,4,6,9,16]
    cv = StratifiedKFold(5,shuffle=True,random_state=17)
    train, valid = validation_curve(DecisionTreeClassifier(random_state=17),x,y,
                                     param_name="max_depth",param_range=depths,cv=cv)
    j=int(valid.mean(axis=1).argmax())
    return {"depths":depths,"train":[round(float(a),3) for a in train.mean(axis=1)],
            "validation":[round(float(a),3) for a in valid.mean(axis=1)],"selectedDepth":depths[j],
            "seed":19,"samples":len(y)}

def regression():
    examples = json.loads((ROOT/"resolution.json").read_text())
    x,y = np.array(examples["train"]).T
    h,actual = np.array(examples["development"]).T
    model = LinearRegression().fit(x[:,None],y)
    predicted = model.predict(h[:,None])
    def scores(p):
        return {"mae":round(mean_absolute_error(actual,p),2),
                "rmse":round(root_mean_squared_error(actual,p),2)}
    return {"intercept":round(float(model.intercept_),2),"slope":round(float(model.coef_[0]),2),
            "baseline":scores(np.full_like(actual,y.mean())),"regression":scores(predicted),
            "rows":[[float(a),float(b),round(float(c),2),round(float(b-c),2)] for a,b,c in zip(h,actual,predicted)]}

def cluster():
    # Descriptions only. No urgency labels are passed to the estimator.
    texts = json.loads((ROOT/"clusters.json").read_text())
    v = TfidfVectorizer(norm="l2")
    x = v.fit_transform(texts)
    k = KMeans(n_clusters=3,random_state=21,n_init=20).fit(x)
    terms = v.get_feature_names_out()
    return {"groups":[{"id":int(i),"terms":[str(terms[j]) for j in np.argsort(k.cluster_centers_[i])[-3:][::-1]],
                       "tickets":[t for t,l in zip(texts,k.labels_) if l==i]} for i in range(k.n_clusters)]}

def route(text):
    if not isinstance(text,str) or not text.strip() or len(text)>500:
        return {"status":"invalid","queue":None}
    model = joblib.load(MODEL)
    v = model.named_steps["countvectorizer"]
    if v.transform([text]).nnz == 0:
        return {"status":"review","queue":None,"reason":"unfamiliar vocabulary"}
    p = model.predict_proba([text])[0]
    i = int(p.argmax())
    if p[i] < .70:
        return {"status":"review","queue":None,"reason":"low score","score":round(float(p[i]),3)}
    return {"status":"routed","queue":str(model.classes_[i]),"score":round(float(p[i]),3)}

def final():
    cases=[None," ","gyroscope nebula","invoice production","checkout down"]
    labeled=[{"text":t,"actual":a,**route(t)} for t,a in rows("final")]
    routed=[r for r in labeled if r["status"]=="routed"]
    return {"edges":[{"text":t,**route(t)} for t in cases],"final":labeled,
            "coverage":[len(routed),len(labeled)],
            "correctAmongRouted":[sum(r["actual"]==r["queue"] for r in routed),len(routed)]}

if __name__=="__main__":
    cmd=sys.argv[1]
    if cmd=="route":
        try:
            value=json.loads(sys.stdin.read())
        except json.JSONDecodeError:
            value=None
        output=route(value)
    else:
        output={"fit":fit,"metrics":metrics,"complexity":complexity,"regression":regression,
                "cluster":cluster,"final":final}[cmd]()
        output["scikitLearn"]=sklearn.__version__
    print(json.dumps(output,indent=2))

