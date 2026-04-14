package com.payflow.settlement;

class InsufficientFundsException extends RuntimeException {
    InsufficientFundsException() {
        super("insufficient funds");
    }
}
