/* ----------------------------------------------------------------------------
 * This file was automatically generated by SWIG (http://www.swig.org).
 * Version 3.0.2
 *
 * Do not make changes to this file unless you know what you are doing--modify
 * the SWIG interface file instead.
 * ----------------------------------------------------------------------------- */

namespace Firebase.Firestore {

internal class QueryProxy : global::System.IDisposable {
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;
  protected bool swigCMemOwn;

  internal QueryProxy(global::System.IntPtr cPtr, bool cMemoryOwn) {
    swigCMemOwn = cMemoryOwn;
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
  }

  internal static global::System.Runtime.InteropServices.HandleRef getCPtr(QueryProxy obj) {
    return (obj == null) ? new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero) : obj.swigCPtr;
  }

  ~QueryProxy() {
    Dispose();
  }

  public virtual void Dispose() {
    lock (FirebaseApp.disposeLock) {
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          FirestoreCppPINVOKE.delete_QueryProxy(swigCPtr);
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
            null, global::System.IntPtr.Zero);
      }
      global::System.GC.SuppressFinalize(this);
    }
  }

  public virtual QueryProxy WhereEqualTo(string field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereEqualTo__SWIG_0(swigCPtr, field, FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereEqualTo(FieldPathProxy field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereEqualTo__SWIG_1(swigCPtr, FieldPathProxy.getCPtr(field), FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereNotEqualTo(string field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereNotEqualTo__SWIG_0(swigCPtr, field, FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereNotEqualTo(FieldPathProxy field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereNotEqualTo__SWIG_1(swigCPtr, FieldPathProxy.getCPtr(field), FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereLessThan(string field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereLessThan__SWIG_0(swigCPtr, field, FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereLessThan(FieldPathProxy field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereLessThan__SWIG_1(swigCPtr, FieldPathProxy.getCPtr(field), FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereLessThanOrEqualTo(string field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereLessThanOrEqualTo__SWIG_0(swigCPtr, field, FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereLessThanOrEqualTo(FieldPathProxy field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereLessThanOrEqualTo__SWIG_1(swigCPtr, FieldPathProxy.getCPtr(field), FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereGreaterThan(string field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereGreaterThan__SWIG_0(swigCPtr, field, FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereGreaterThan(FieldPathProxy field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereGreaterThan__SWIG_1(swigCPtr, FieldPathProxy.getCPtr(field), FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereGreaterThanOrEqualTo(string field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereGreaterThanOrEqualTo__SWIG_0(swigCPtr, field, FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereGreaterThanOrEqualTo(FieldPathProxy field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereGreaterThanOrEqualTo__SWIG_1(swigCPtr, FieldPathProxy.getCPtr(field), FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereArrayContains(string field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereArrayContains__SWIG_0(swigCPtr, field, FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy WhereArrayContains(FieldPathProxy field, FieldValueProxy value) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_WhereArrayContains__SWIG_1(swigCPtr, FieldPathProxy.getCPtr(field), FieldValueProxy.getCPtr(value)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy OrderBy(string field, QueryProxy.Direction direction) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_OrderBy__SWIG_0(swigCPtr, field, (int)direction), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy OrderBy(string field) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_OrderBy__SWIG_1(swigCPtr, field), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy OrderBy(FieldPathProxy field, QueryProxy.Direction direction) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_OrderBy__SWIG_2(swigCPtr, FieldPathProxy.getCPtr(field), (int)direction), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy OrderBy(FieldPathProxy field) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_OrderBy__SWIG_3(swigCPtr, FieldPathProxy.getCPtr(field)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy Limit(int limit) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_Limit(swigCPtr, limit), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy LimitToLast(int limit) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_LimitToLast(swigCPtr, limit), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy StartAt(DocumentSnapshotProxy snapshot) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_StartAt(swigCPtr, DocumentSnapshotProxy.getCPtr(snapshot)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy StartAfter(DocumentSnapshotProxy snapshot) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_StartAfter(swigCPtr, DocumentSnapshotProxy.getCPtr(snapshot)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy EndBefore(DocumentSnapshotProxy snapshot) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_EndBefore(swigCPtr, DocumentSnapshotProxy.getCPtr(snapshot)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual QueryProxy EndAt(DocumentSnapshotProxy snapshot) {
    QueryProxy ret = new QueryProxy(FirestoreCppPINVOKE.QueryProxy_EndAt(swigCPtr, DocumentSnapshotProxy.getCPtr(snapshot)), true);
    if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return ret;
  }

  public virtual System.Threading.Tasks.Task<QuerySnapshotProxy> GetAsync(Source source) {
    var future = FirestoreCppPINVOKE.QueryProxy_Get__SWIG_0(swigCPtr, (int)source);
    
      if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return Future_QuerySnapshot.GetTask(new Future_QuerySnapshot(future, true));
  }

  public virtual System.Threading.Tasks.Task<QuerySnapshotProxy> GetAsync() {
    var future = FirestoreCppPINVOKE.QueryProxy_Get__SWIG_1(swigCPtr);
    
      if (FirestoreCppPINVOKE.SWIGPendingException.Pending) throw FirestoreCppPINVOKE.SWIGPendingException.Retrieve();
    return Future_QuerySnapshot.GetTask(new Future_QuerySnapshot(future, true));
  }

  public enum Direction {
    Ascending,
    Descending
  }

}

}