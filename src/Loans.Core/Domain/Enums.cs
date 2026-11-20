namespace Loans.Core.Domain;

public enum EntityType { Llc, Corporation, Partnership }

public enum LoanProduct { TermLoan, CreMortgage }

public enum CollateralType { RealEstate, Equipment, BlanketLien }

public enum ApplicationStatus { Submitted, InUnderwriting, Approved, Declined }

public enum Recommendation { Approve, ApproveWithConditions, Decline }

public enum DecisionOutcome { Approve, ApproveWithConditions, Decline }

public enum LoanStatus { Current, PastDue, PaidOff }

public enum PaymentStatus { OnTime, Late, Missed }

public enum PolicyException { DscrBelowMinimum, LtvAboveMaximum, InsufficientTimeInBusiness }
