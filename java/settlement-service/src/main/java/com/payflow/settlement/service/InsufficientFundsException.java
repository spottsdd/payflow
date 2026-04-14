package com.payflow.settlement.service;

class InsufficientFundsException extends RuntimeException {
    InsufficientFundsException() {
        super("insufficient funds");
    }
}
