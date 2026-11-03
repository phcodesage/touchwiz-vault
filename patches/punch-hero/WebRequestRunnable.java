package com.unity3d.services.core.request;

import android.os.Bundle;
import com.unity3d.services.core.log.DeviceLog;
import java.net.MalformedURLException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public class WebRequestRunnable implements Runnable {
    private WebRequest _currentRequest;
    private boolean _canceled = false;
    private final String _url;
    private final String _type;
    private final String _body;
    private final int _connectTimeout;
    private final int _readTimeout;
    private final Map<String, List<String>> _headers;
    private final IWebRequestListener _listener;

    public WebRequestRunnable(String url, String type, String body, int connectTimeout, int readTimeout, Map<String, List<String>> headers, IWebRequestListener listener) {
        this._url = url;
        this._type = type;
        this._body = body;
        this._connectTimeout = connectTimeout;
        this._readTimeout = readTimeout;
        this._headers = headers;
        this._listener = listener;
    }

    @Override
    public void run() {
        DeviceLog.debug("Handling request message: " + this._url + " type=" + this._type);
        try {
            makeRequest(this._url, this._type, this._headers, this._body, this._connectTimeout, this._readTimeout);
        } catch (MalformedURLException e) {
            DeviceLog.exception("Malformed URL", e);
            onFailed("Malformed URL");
        }
    }

    public void setCancelStatus(boolean canceled) {
        this._canceled = canceled;
        if (this._canceled && this._currentRequest != null) {
            this._currentRequest.cancel();
        }
    }

    private void makeRequest(String url, String type, Map<String, List<String>> headers, String body, int connectTimeout, int readTimeout) throws MalformedURLException {
        if (this._canceled) {
            return;
        }
        this._currentRequest = new WebRequest(url, type, headers, connectTimeout, readTimeout);
        if (body != null) {
            this._currentRequest.setBody(body);
        }
        String response;
        try {
            response = this._currentRequest.makeRequest();
        } catch (Exception e) {
            DeviceLog.exception("Error completing request", e);
            onFailed(e.getClass().getName() + ": " + e.getMessage());
            return;
        }
        if (this._currentRequest.isCanceled()) {
            return;
        }
        Bundle data = new Bundle();
        Map<String, List<String>> responseHeaders = this._currentRequest.getResponseHeaders();
        if (responseHeaders != null) {
            for (String key : responseHeaders.keySet()) {
                if (key != null && !key.contentEquals("null")) {
                    List<String> values = responseHeaders.get(key);
                    if (values != null) {
                        String[] valueArray = new String[values.size()];
                        for (int valueIdx = 0; valueIdx < values.size(); valueIdx++) {
                            valueArray[valueIdx] = values.get(valueIdx);
                        }
                        data.putStringArray(key, valueArray);
                    }
                }
            }
        }
        if (this._currentRequest.isCanceled()) {
            return;
        }
        onSucceed(response, this._currentRequest.getResponseCode(), getResponseHeaders(data));
    }

    private void onSucceed(String response, int responseCode, Map<String, List<String>> headers) {
        if (response != null && response.contains("\"placementType\":\"bidding\"")) {
            DeviceLog.info("Patching UnityAds placement config from bidding to waterfall for standalone UnityAds SDK");
            response = response
                .replace("\"placementType\":\"bidding\"", "\"placementType\":\"waterfall\"")
                .replace("\"headerBiddingEnabled\":true", "\"headerBiddingEnabled\":false");
        }
        this._listener.onComplete(this._url, response, responseCode, headers);
    }

    private void onFailed(String error) {
        this._listener.onFailed(this._url, error);
    }

    private Map<String, List<String>> getResponseHeaders(Bundle resultData) {
        Map<String, List<String>> responseHeaders = null;
        if (resultData.size() > 0) {
            responseHeaders = new HashMap<>();
            for (String k : resultData.keySet()) {
                String[] tempAr = resultData.getStringArray(k);
                if (tempAr != null) {
                    responseHeaders.put(k, new ArrayList<>(Arrays.asList(tempAr)));
                }
            }
        }
        return responseHeaders;
    }
}
