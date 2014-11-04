﻿/// <reference path="globals.ts"/>
define(["require", "exports", "Framework/Signum.Web/Signum/Scripts/Entities", "Framework/Signum.Web/Signum/Scripts/Validator", "Framework/Signum.Web/Signum/Scripts/Navigator", "Framework/Signum.Web/Signum/Scripts/Finder"], function(require, exports, Entities, Validator, Navigator, Finder) {
    function executeDefault(options) {
        options = $.extend({
            avoidValidate: false,
            validationOptions: {},
            isLite: false
        }, options);

        if (!exports.confirmIfNecessary(options))
            return Promise.reject("confirmation");

        return exports.entityIsValidOrLite(options).then(function () {
            return exports.executeAjax(options).then(function (eHtml) {
                Navigator.reload(eHtml);
                exports.notifyExecuted();
            });
        });
    }
    exports.executeDefault = executeDefault;

    function executeAjax(options) {
        options = $.extend({
            controllerUrl: SF.Urls.operationExecute,
            isLite: false,
            isNavigatePopup: Navigator.isNavigatePopup(options.prefix)
        }, options);

        return SF.ajaxPost({ url: options.controllerUrl, data: exports.entityRequestData(options) }).then(function (result) {
            Validator.assertModelStateErrors(result, options.prefix);
            return Entities.EntityHtml.fromHtml(options.prefix, result);
        });
    }
    exports.executeAjax = executeAjax;

    function executeDefaultContextual(options) {
        if (!exports.confirmIfNecessary(options))
            return Promise.reject("confirmation");

        return exports.executeAjaxContextual(options).then(function (result) {
            if (result)
                exports.markCells(options.prefix);
        });
    }
    exports.executeDefaultContextual = executeDefaultContextual;

    function executeAjaxContextual(options, runtimeInfo) {
        options = $.extend({
            controllerUrl: SF.Urls.operationExecute,
            avoidReturnView: true,
            isLite: true
        }, options);

        return SF.ajaxPost({ url: options.controllerUrl, data: exports.contextualRequestData(options, null, runtimeInfo) }).then(SF.isEmpty);
    }
    exports.executeAjaxContextual = executeAjaxContextual;

    function constructFromDefault(options, openNewWindowOrEvent) {
        options = $.extend({
            avoidValidate: false,
            validationOptions: {},
            isLite: true
        }, options);

        if (!exports.confirmIfNecessary(options))
            return Promise.reject("confirmation");

        return exports.entityIsValidOrLite(options).then(function () {
            if (Navigator.isOpenNewWindow(openNewWindowOrEvent))
                exports.constructFromSubmit(options);
            else
                return exports.constructFromAjax(options, exports.getNewPrefix(options)).then(function (eHtml) {
                    return exports.openPopup(eHtml);
                });
        });
    }
    exports.constructFromDefault = constructFromDefault;

    function constructFromAjax(options, newPrefix) {
        options = $.extend({
            controllerUrl: SF.Urls.operationConstructFrom,
            isLite: true
        }, options);

        return SF.ajaxPost({ url: options.controllerUrl, data: exports.entityRequestData(options, newPrefix) }).then(function (html) {
            return Entities.EntityHtml.fromHtml(newPrefix, html);
        });
    }
    exports.constructFromAjax = constructFromAjax;

    function constructFromSubmit(options) {
        options = $.extend({
            controllerUrl: SF.Urls.operationConstructFrom,
            isLite: true
        }, options);

        SF.submitOnly(options.controllerUrl, exports.entityRequestData(options, ""), true);
    }
    exports.constructFromSubmit = constructFromSubmit;

    function constructFromDefaultContextual(options, openNewWindowOrEvent) {
        if (!exports.confirmIfNecessary(options))
            return Promise.reject("confirmation");

        if (Navigator.isOpenNewWindow(openNewWindowOrEvent)) {
            exports.markCells(options.prefix);
            exports.constructFromSubmitContextual(options);
        } else {
            return exports.constructFromAjaxContextual(options, exports.getNewPrefix(options)).then(function (eHtml) {
                exports.markCells(options.prefix);
                return exports.openPopup(eHtml);
            });
        }
    }
    exports.constructFromDefaultContextual = constructFromDefaultContextual;

    function constructFromAjaxContextual(options, newPrefix, runtimeInfo) {
        options = $.extend({
            controllerUrl: SF.Urls.operationConstructFrom,
            isLite: true
        }, options);

        return SF.ajaxPost({ url: options.controllerUrl, data: exports.contextualRequestData(options, newPrefix, runtimeInfo) }).then(function (html) {
            return Entities.EntityHtml.fromHtml(newPrefix, html);
        });
    }
    exports.constructFromAjaxContextual = constructFromAjaxContextual;

    function constructFromSubmitContextual(options, runtimeInfo) {
        options = $.extend({
            controllerUrl: SF.Urls.operationConstructFrom,
            isLite: true
        }, options);

        SF.submitOnly(options.controllerUrl, exports.contextualRequestData(options, "", runtimeInfo), true);
    }
    exports.constructFromSubmitContextual = constructFromSubmitContextual;

    function deleteDefault(options) {
        options = $.extend({
            avoidValidate: true,
            isLite: true
        }, options);

        if (!exports.confirmIfNecessary(options))
            return Promise.reject("confirmation");

        return exports.entityIsValidOrLite(options).then(function () {
            return exports.deleteAjax(options);
        }).then(function () {
            //ajax prefilter will take redirect
            if (options.prefix) {
                Navigator.closePopup(options.prefix);
            }
        });
    }
    exports.deleteDefault = deleteDefault;

    function deleteAjax(options) {
        options = $.extend({
            controllerUrl: SF.Urls.operationDelete,
            avoidReturnRedirect: !!options.prefix,
            isLite: true
        }, options);

        return SF.ajaxPost({ url: options.controllerUrl, data: exports.entityRequestData(options) });
    }
    exports.deleteAjax = deleteAjax;

    function deleteDefaultContextual(options) {
        options = $.extend({
            isLite: true
        }, options);

        if (!exports.confirmIfNecessary(options))
            return Promise.reject("confirmation");

        return exports.deleteAjaxContextual(options).then(function (result) {
            exports.markCells(options.prefix);
        });
    }
    exports.deleteDefaultContextual = deleteDefaultContextual;

    function deleteAjaxContextual(options, runtimeInfo) {
        options = $.extend({
            controllerUrl: SF.Urls.operationDelete,
            avoidReturnRedirect: true,
            isLite: true
        }, options);

        return SF.ajaxPost({ url: options.controllerUrl, data: exports.contextualRequestData(options, null, runtimeInfo) });
    }
    exports.deleteAjaxContextual = deleteAjaxContextual;

    function constructFromManyDefault(options, openNewWindowOrEvent) {
        if (!exports.confirmIfNecessary(options))
            return Promise.reject("confirmation");

        if (Navigator.isOpenNewWindow(openNewWindowOrEvent)) {
            exports.markCells(options.prefix);
            exports.constructFromManySubmit(options);
        } else {
            return exports.constructFromManyAjax(options, exports.getNewPrefix(options)).then(function (eHtml) {
                exports.markCells(options.prefix);
                return exports.openPopup(eHtml);
            });
        }
    }
    exports.constructFromManyDefault = constructFromManyDefault;

    function constructFromManyAjax(options, newPrefix) {
        options = $.extend({
            isLite: true,
            controllerUrl: SF.Urls.operationConstructFromMany
        }, options);

        return SF.ajaxPost({ url: options.controllerUrl, data: exports.constructFromManyRequestData(options, newPrefix) }).then(function (html) {
            return Entities.EntityHtml.fromHtml(newPrefix, html);
        });
    }
    exports.constructFromManyAjax = constructFromManyAjax;

    function constructFromManySubmit(options) {
        options = $.extend({
            isLite: true,
            controllerUrl: SF.Urls.operationConstructFromMany
        }, options);

        SF.submitOnly(options.controllerUrl, exports.constructFromManyRequestData(options, ""), true);
    }
    exports.constructFromManySubmit = constructFromManySubmit;

    function confirmIfNecessary(options) {
        return !options.confirmMessage || confirm(options.confirmMessage);
    }
    exports.confirmIfNecessary = confirmIfNecessary;

    function openPopup(entityHtml) {
        exports.notifyExecuted();
        return Navigator.navigatePopup(entityHtml);
    }
    exports.openPopup = openPopup;

    function markCells(prefix) {
        $("tr.active").addClass("sf-entity-ctxmenu-success");
        exports.notifyExecuted();
    }
    exports.markCells = markCells;

    function notifyExecuted() {
        SF.Notify.info(lang.signum.executed, 2000);
    }
    exports.notifyExecuted = notifyExecuted;

    function getNewPrefix(options) {
        return options.prefix.child("New");
    }
    exports.getNewPrefix = getNewPrefix;

    function entityRequestData(options, newPrefix) {
        var result = exports.baseRequestData(options, newPrefix);

        var formValues = options.isLite ? Validator.getFormValuesLite(options.prefix) : Validator.getFormValues(options.prefix);

        formValues[Entities.Keys.viewMode] = options.isNavigatePopup ? "Navigate" : "View";

        return $.extend(result, formValues);
    }
    exports.entityRequestData = entityRequestData;

    function constructFromManyRequestData(options, newPrefix, liteKey) {
        var result = exports.baseRequestData(options, newPrefix);

        if (!liteKey) {
            var items = Finder.SearchControl.getSelectedItems(options.prefix);
            liteKey = items.map(function (i) {
                return i.key();
            });
        }

        result["liteKeys"] = liteKey.join(",");

        return result;
    }
    exports.constructFromManyRequestData = constructFromManyRequestData;

    function contextualRequestData(options, newPrefix, runtimeInfo) {
        var result = exports.baseRequestData(options, newPrefix);

        if (!runtimeInfo) {
            var items = Finder.SearchControl.getSelectedItems(options.prefix);

            if (items.length > 1)
                throw new Error("just one entity should have been selected");

            runtimeInfo = items[0].runtimeInfo;
        }

        result[options.prefix.child(Entities.Keys.runtimeInfo)] = runtimeInfo.toString();

        return result;
    }
    exports.contextualRequestData = contextualRequestData;

    function baseRequestData(options, newPrefix) {
        var formValues = Validator.getFormBasics();

        formValues = $.extend({
            isLite: options.isLite,
            operationFullKey: options.operationKey,
            newprefix: newPrefix,
            prefix: options.prefix
        }, formValues);

        if (options.avoidReturnRedirect)
            formValues["sfAvoidReturnRedirect"] = true;

        if (options.avoidReturnView)
            formValues["sfAvoidReturnView"] = true;

        return $.extend(formValues, options.requestExtraJsonData);
    }
    exports.baseRequestData = baseRequestData;

    function entityIsValidOrLite(options) {
        if (options.isLite || options.avoidValidate)
            return Promise.resolve(null);

        var valOptions = $.extend({ prefix: options.prefix }, options.validationOptions);

        return Validator.entityIsValid(valOptions);
    }
    exports.entityIsValidOrLite = entityIsValidOrLite;

    function validateAndSubmit(options) {
        if (exports.entityIsValidOrLite(options))
            exports.submit(options);
    }
    exports.validateAndSubmit = validateAndSubmit;

    function submit(options) {
        var mainControl = options.prefix ? $("#{0}_divMainControl".format(options.prefix)) : $("#divMainControl");

        var $form = mainControl.closest("form");
        $form.append(SF.hiddenInput('isLite', options.isLite) + SF.hiddenInput('operationFullKey', options.operationKey) + SF.hiddenInput("prefix", options.prefix));

        if (!SF.isEmpty(options.prefix)) {
            //Check runtimeInfo present => if it's a popup from a LineControl it will not be
            var myRuntimeInfoKey = options.prefix.child(Entities.Keys.runtimeInfo);
            if (myRuntimeInfoKey.tryGet().length == 0) {
                SF.hiddenInput(myRuntimeInfoKey, mainControl.data("runtimeinfo"));
            }
        }

        SF.submit(options.controllerUrl, options.requestExtraJsonData, $form);

        return false;
    }
    exports.submit = submit;
});
//# sourceMappingURL=Operations.js.map
